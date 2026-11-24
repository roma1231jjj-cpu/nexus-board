package ru.nexus.board;

import android.content.Context;
import android.content.res.Configuration;
import android.graphics.Typeface;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.appcompat.app.AppCompatActivity;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.HashSet;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class ChatActivity extends AppCompatActivity {

    private static final long POLL_MS = 5000L;
    private static final int MAX_TEXT = 4000;

    private Prefs prefs;
    private final ExecutorService pool = Executors.newSingleThreadExecutor();
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final Set<String> seen = new HashSet<>();

    private ChatAdapter adapter;
    private RecyclerView list;
    private EditText input;
    private TextView status;
    private LinearLayout empty;

    private volatile boolean loading = false;
    private volatile boolean boardTried = false;
    private long last = 0L;
    private int tmpId = 0;

    @Override
    protected void onCreate(Bundle b) {
        prefs = new Prefs(this);
        applyTheme();
        super.onCreate(b);
        setContentView(R.layout.activity_chat);

        NxBackdrop.apply(findViewById(R.id.root));

        list = findViewById(R.id.chat_list);
        input = findViewById(R.id.chat_input);
        status = findViewById(R.id.chat_status);
        empty = findViewById(R.id.chat_empty);

        adapter = new ChatAdapter();
        LinearLayoutManager lm = new LinearLayoutManager(this);
        lm.setStackFromEnd(false);
        list.setLayoutManager(lm);
        list.setAdapter(adapter);

        TextView title = findViewById(R.id.chat_title);
        title.setText(R.string.chat_title);

        findViewById(R.id.btn_back).setOnClickListener(v -> finish());
        findViewById(R.id.btn_send).setOnClickListener(v -> send());

        input.setOnEditorActionListener((v, actionId, event) -> {
            if (actionId == EditorInfo.IME_ACTION_SEND) {
                send();
                return true;
            }
            return false;
        });

        status.setVisibility(View.VISIBLE);
        status.setText(R.string.loading);
        status.setOnClickListener(null);
    }

    private void applyTheme() {
        String t = prefs.theme();
        if ("frost".equals(t)) setTheme(R.style.Nexus_Frost);
        else if ("graphite".equals(t)) setTheme(R.style.Nexus_Graphite);
        else setTheme(R.style.Nexus_Midnight);
    }

    @Override
    protected void attachBaseContext(Context base) {
        String tag = "ru".equals(new Prefs(base).lang()) ? "ru" : "en";
        Configuration cfg = new Configuration(base.getResources().getConfiguration());
        cfg.setLocale(new Locale(tag));
        super.attachBaseContext(base.createConfigurationContext(cfg));
    }

    private int warningColor() {
        return "frost".equals(prefs.theme()) ? 0xFFB45309 : 0xFFF0B429;
    }

    private void showStatus(int res, final Runnable retry) {
        status.setVisibility(View.VISIBLE);
        status.setText(res);
        status.setTextColor(warningColor());
        status.setTypeface(Typeface.DEFAULT_BOLD);
        status.setOnClickListener(retry == null ? null : v -> retry.run());
    }

    private void hideStatus() {
        status.setVisibility(View.GONE);
        status.setOnClickListener(null);
        status.setTypeface(Typeface.DEFAULT);
        status.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
    }

    private void updateEmpty() {
        empty.setVisibility(adapter.getItemCount() == 0 ? View.VISIBLE : View.GONE);
    }

    private void scrollBottom() {
        if (adapter.getItemCount() == 0) return;
        list.scrollToPosition(adapter.getItemCount() - 1);
    }

    private void refresh() {
        final String board = prefs.boardId();
        if (board.isEmpty()) {
            if (boardTried) {
                showStatus(R.string.chat_need_board, this::refresh);
                updateEmpty();
                return;
            }
            boardTried = true;
            registerBoard();
            return;
        }
        if (loading) return;
        loading = true;
        final String base = prefs.base();
        final long since = last;
        pool.execute(() -> {
            JSONArray messages = null;
            long next = since;
            try {
                JSONObject o = Api.json(base, "/api/c/list?board=" + board + "&since=" + since + "&limit=200", "");
                messages = o.optJSONArray("messages");
                long l = o.optLong("last", 0L);
                if (l > next) next = l;
            } catch (Exception ignored) {
            }
            final JSONArray fMessages = messages;
            final long fNext = next;
            ui.post(() -> {
                loading = false;
                if (fMessages == null) {
                    showStatus(R.string.chat_net_error, this::refresh);
                    updateEmpty();
                    return;
                }
                if (fNext > last) last = fNext;
                boolean added = merge(fMessages);
                hideStatus();
                updateEmpty();
                if (added) scrollBottom();
            });
        });
    }

    private void registerBoard() {
        final String base = prefs.base();
        pool.execute(() -> {
            String id = "";
            try {
                JSONObject o = Api.postJson(base + "/api/b/new", "", "{}");
                id = o.optString("id", "");
            } catch (Exception ignored) {
            }
            final String fId = id;
            ui.post(() -> {
                if (fId.isEmpty()) {
                    showStatus(R.string.chat_net_error, this::refresh);
                    updateEmpty();
                    return;
                }
                prefs.boardId(fId);
                refresh();
            });
        });
    }

    private boolean merge(JSONArray arr) {
        boolean added = false;
        for (int i = 0; i < arr.length(); i++) {
            JSONObject m = arr.optJSONObject(i);
            if (m == null) continue;
            String id = m.optString("id", "");
            if (id.isEmpty()) id = m.optString("from", "x") + "-" + m.optLong("ts", 0L);
            if (!seen.add(id)) continue;
            ChatAdapter.Msg msg = new ChatAdapter.Msg();
            msg.id = id;
            msg.owner = "owner".equals(m.optString("from", ""));
            msg.text = m.optString("text", "");
            msg.ts = m.optLong("ts", 0L);
            adapter.add(msg);
            added = true;
        }
        return added;
    }

    private void send() {
        final String text = input.getText().toString().trim();
        if (text.isEmpty()) return;
        final String board = prefs.boardId();
        if (board.isEmpty()) {
            showStatus(R.string.chat_need_board, this::refresh);
            return;
        }
        input.setText("");
        final ChatAdapter.Msg msg = new ChatAdapter.Msg();
        msg.id = "tmp-" + (++tmpId);
        msg.owner = true;
        msg.text = text.length() > MAX_TEXT ? text.substring(0, MAX_TEXT) : text;
        msg.ts = System.currentTimeMillis();
        msg.pending = true;
        seen.add(msg.id);
        adapter.add(msg);
        hideStatus();
        updateEmpty();
        scrollBottom();

        final String base = prefs.base();
        pool.execute(() -> {
            String realId = "";
            long ts = msg.ts;
            try {
                JSONObject body = new JSONObject();
                body.put("board", board);
                body.put("from", "owner");
                body.put("text", msg.text);
                JSONObject o = Api.postJson(base + "/api/c/send", "", body.toString());
                realId = o.optString("id", "");
                ts = o.optLong("ts", ts);
            } catch (Exception ignored) {
            }
            final String fId = realId;
            final long fTs = ts;
            ui.post(() -> {
                if (fId.isEmpty()) {
                    adapter.remove(msg);
                    seen.remove(msg.id);
                    if (input.getText().toString().trim().isEmpty()) {
                        input.setText(msg.text);
                        input.setSelection(input.getText().length());
                    }
                    showStatus(R.string.chat_send_failed, this::refresh);
                    updateEmpty();
                    return;
                }
                seen.add(fId);
                msg.id = fId;
                msg.pending = false;
                msg.ts = fTs;
                if (fTs > last) last = fTs;
                adapter.touch();
                refresh();
            });
        });
    }

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            refresh();
            ui.postDelayed(this, POLL_MS);
        }
    };

    @Override
    protected void onStart() {
        super.onStart();
        refresh();
        ui.removeCallbacks(tick);
        ui.postDelayed(tick, POLL_MS);
    }

    @Override
    protected void onStop() {
        ui.removeCallbacks(tick);
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        ui.removeCallbacks(tick);
        super.onDestroy();
        pool.shutdownNow();
    }
}
