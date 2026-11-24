package ru.nexus.board;

import android.content.Context;
import android.content.Intent;
import android.content.res.Configuration;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.FileProvider;
import androidx.core.widget.NestedScrollView;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends AppCompatActivity {

    private Prefs prefs;
    private final ExecutorService pool = Executors.newFixedThreadPool(2);
    private static final long REFRESH_MS = 10000L;
    private static final long REFRESH_SLOW_MS = 30000L;
    private volatile boolean loading = false;
    private volatile boolean lastFailed = false;
    private final Handler ui = new Handler(Looper.getMainLooper());

    private BoardAdapter adapter;
    private RecyclerView list;
    private SwipeRefreshLayout swipe;
    private NestedScrollView trainScroll;
    private LinearLayout chips;
    private HorizontalScrollView chipsWrap;
    private List<String[]> sections = new ArrayList<>();
    private String activeSection = "";
    private JSONObject lastTasks;
    private TextView status, tabBoard, tabTrain;
    private TextView trState, trRun, trStage, trStep, trLoss, trEta, trRate, trSteps, trChain, trTail;
    private ProgressBar trBar;
    private SparkView trSpark;

    private boolean showTrain = false;
    private String pendingUpdate = null;
    private String pendingNotes = "";

    @Override
    protected void onCreate(Bundle b) {
        prefs = new Prefs(this);
        applyTheme();
        super.onCreate(b);
        setContentView(R.layout.activity_main);

        NxBackdrop.apply(findViewById(R.id.root));

        status = findViewById(R.id.status);
        tabBoard = findViewById(R.id.tab_board);
        tabTrain = findViewById(R.id.tab_train);
        list = findViewById(R.id.list);
        swipe = findViewById(R.id.swipe);
        trainScroll = findViewById(R.id.train_scroll);
        chips = findViewById(R.id.chips);
        chipsWrap = findViewById(R.id.chips_wrap);

        trState = findViewById(R.id.tr_state);
        trRun = findViewById(R.id.tr_run);
        trStage = findViewById(R.id.tr_stage);
        trStep = findViewById(R.id.tr_step);
        trLoss = findViewById(R.id.tr_loss);
        trEta = findViewById(R.id.tr_eta);
        trRate = findViewById(R.id.tr_rate);
        trSteps = findViewById(R.id.tr_steps);
        trChain = findViewById(R.id.tr_chain);
        trTail = findViewById(R.id.tr_tail);
        trBar = findViewById(R.id.tr_bar);
        trSpark = findViewById(R.id.tr_spark);

        adapter = new BoardAdapter();
        list.setLayoutManager(new LinearLayoutManager(this));
        list.setAdapter(adapter);

        int accent = NxBackdrop.color(this, R.attr.nxPrimary);
        swipe.setColorSchemeColors(accent);
        swipe.setProgressBackgroundColorSchemeColor(NxBackdrop.color(this, R.attr.nxSurface));
        swipe.setOnRefreshListener(this::load);

        findViewById(R.id.btn_theme).setOnClickListener(v -> nextTheme());
        findViewById(R.id.btn_lang).setOnClickListener(v -> switchLang());
        findViewById(R.id.btn_key).setOnClickListener(v -> openConnect());
        findViewById(R.id.btn_update).setOnClickListener(v -> {
            if (pendingUpdate != null) downloadUpdate();
            else checkUpdates(true);
        });

        TextView langBtn = findViewById(R.id.btn_lang);
        langBtn.setText("ru".equals(prefs.lang()) ? "EN" : "RU");

        tabBoard.setOnClickListener(v -> selectTab(false));
        tabTrain.setOnClickListener(v -> selectTab(true));
        if (!prefs.nexusMode()) {
            findViewById(R.id.tabs).setVisibility(View.GONE);
            selectTab(false);
        }
        findViewById(R.id.empty_btn).setOnClickListener(v -> openConnect());

        status.setOnClickListener(v -> openConnect());
        status.setOnLongClickListener(v -> {
            openConnect();
            return true;
        });

        findViewById(R.id.title).setOnLongClickListener(v -> {
            openSettings();
            return true;
        });

        selectTab(prefs.nexusMode() && prefs.trainTab());
        load();
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

    private void nextTheme() {
        String t = prefs.theme();
        if ("midnight".equals(t)) prefs.theme("frost");
        else if ("frost".equals(t)) prefs.theme("graphite");
        else prefs.theme("midnight");
        recreate();
    }

    private void switchLang() {
        prefs.lang("ru".equals(prefs.lang()) ? "en" : "ru");
        recreate();
    }

    private void selectTab(boolean train) {
        showTrain = train;
        prefs.trainTab(train);
        trainScroll.setVisibility(train ? View.VISIBLE : View.GONE);
        swipe.setVisibility(train ? View.GONE : View.VISIBLE);
        int on = NxBackdrop.color(this, R.attr.nxPrimary);
        int onText = NxBackdrop.color(this, R.attr.nxOnPrimary);
        int dim = NxBackdrop.color(this, R.attr.nxTextDim);
        tabBoard.setBackgroundResource(train ? R.drawable.nx_pill_off : R.drawable.nx_pill_on);
        tabBoard.setTextColor(train ? dim : onText);
        tabTrain.setBackgroundResource(train ? R.drawable.nx_pill_on : R.drawable.nx_pill_off);
        tabTrain.setTextColor(train ? onText : dim);
    }

    private void load() {
        final String key = prefs.key();
        final String base = prefs.base();
        if (!prefs.nexusMode() && prefs.boardId().isEmpty()) registerBoard();
        final String board = prefs.boardId();
        if (loading) return;
        if (board.isEmpty() && key.isEmpty()) {
            status.setText(R.string.connecting);
            return;
        }
        loading = true;
        status.setText(R.string.loading);
        swipe.setRefreshing(true);
        pool.execute(() -> {
            JSONObject tasks = null;
            JSONObject train = null;
            String error = null;
            boolean auth = false;
            final boolean nexus = !key.isEmpty();
            try {
                tasks = nexus
                        ? Api.json(base, "/api/tasks", key)
                        : Api.json(base, "/api/b/tasks?b=" + board, "");
                if (!nexus && tasks != null) {
                    JSONArray ta = tasks.optJSONArray("tasks");
                    if (ta == null || ta.length() == 0) {
                        try {
                            Api.postJson(base + "/api/b/seed?b=" + board, "", "{}");
                            tasks = Api.json(base, "/api/b/tasks?b=" + board, "");
                        } catch (Exception ignored) { }
                    }
                }
            } catch (Api.AuthException e) {
                auth = true;
            } catch (Exception e) {
                error = e.getMessage();
            }
            if (nexus) {
                try {
                    train = Api.json(base, "/api/train", key);
                } catch (Api.AuthException e) {
                    auth = true;
                } catch (Exception e) {
                    if (error == null) error = e.getMessage();
                }
            }
            final JSONObject fTasks = tasks, fTrain = train;
            final String fError = error;
            final boolean fAuth = auth;
            ui.post(() -> {
                loading = false;
                lastFailed = fError != null && fTasks == null && fTrain == null;
                swipe.setRefreshing(false);
                if (fAuth) {
                    askKey();
                    return;
                }
                if (fError != null && fTasks == null && fTrain == null) {
                    status.setText(R.string.net_error_tap);
                    status.setTextColor(warningColor());
                    status.setTypeface(Typeface.DEFAULT_BOLD);
                    status.setOnClickListener(v -> load());
                    return;
                }
                if (fTasks != null) {
                    sections.clear();
                    JSONArray secs = fTasks.optJSONArray("sections");
                    if (secs != null) {
                        for (int i = 0; i < secs.length(); i++) {
                            JSONObject s = secs.optJSONObject(i);
                            if (s == null) continue;
                            sections.add(new String[]{s.optString("id"), s.optString("name")});
                        }
                    }
                    renderChips();
                    renderTasks(fTasks);
                }
                if (fTrain != null) renderTrain(fTrain);
                status.setTypeface(Typeface.DEFAULT);
                status.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
                status.setOnClickListener(v -> openConnect());
                status.setText(getString(R.string.updated, now()));
                checkUpdates();
            });
        });
    }

    private int warningColor() {
        return "frost".equals(prefs.theme()) ? 0xFFB45309 : 0xFFF0B429;
    }

    private String now() {
        return new SimpleDateFormat("HH:mm", Locale.getDefault()).format(new Date());
    }

    private void openConnect() {
        String key = prefs.boardId();
        String body = getString(R.string.connect_body) + (char) 10 + (char) 10 + (key.isEmpty() ? "—" : key);
        new AlertDialog.Builder(this)
                .setTitle(R.string.connect_title)
                .setMessage(body)
                .setPositiveButton(R.string.copy_key, (d, w) -> {
                    if (key.isEmpty()) return;
                    android.content.ClipboardManager cm = (android.content.ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                    cm.setPrimaryClip(android.content.ClipData.newPlainText("Nexus Board key", key));
                    Toast.makeText(this, R.string.key_copied, Toast.LENGTH_SHORT).show();
                })
                .setNegativeButton(R.string.close, null)
                .show();
    }

    private void registerBoard() {
        final String base = prefs.base();
        pool.execute(() -> {
            try {
                JSONObject o = Api.postJson(base + "/api/b/new", "", "{}");
                final String id = o.optString("id", "");
                if (!id.isEmpty()) {
                    prefs.boardId(id);
                    ui.post(this::load);
                }
            } catch (Exception ignored) {
            }
        });
    }

    private void renderChips() {
        chips.removeAllViews();
        int vis = sections.isEmpty() ? View.GONE : View.VISIBLE;
        chipsWrap.setVisibility(vis);
        if (vis == View.GONE) return;

        float d = getResources().getDisplayMetrics().density;
        chips.addView(chip(getString(R.string.all_sections), "", activeSection.isEmpty(), d));
        for (String[] s : sections) {
            chips.addView(chip(s[1], s[0], activeSection.equals(s[0]), d));
        }
    }

    private TextView chip(String label, String id, boolean active, float d) {
        TextView tv = new TextView(this);
        tv.setText(label);
        tv.setTextSize(13f);
        tv.setTypeface(tv.getTypeface(), active ? android.graphics.Typeface.BOLD : android.graphics.Typeface.NORMAL);
        int ph = (int) (14 * d);
        int pv = (int) (8 * d);
        tv.setPadding(ph, pv, ph, pv);
        tv.setTextColor(NxBackdrop.color(this, active ? R.attr.nxOnPrimary : R.attr.nxText));
        tv.setBackgroundResource(active ? R.drawable.nx_pill_on : R.drawable.nx_card);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.setMarginEnd((int) (8 * d));
        tv.setLayoutParams(lp);
        tv.setOnClickListener(v -> {
            activeSection = id;
            renderChips();
            renderTasks(lastTasks);
        });
        return tv;
    }

    private void renderTasks(JSONObject o) {
        if (o == null) return;
        lastTasks = o;
        List<Object> rows = new ArrayList<>();
        JSONArray arr = o.optJSONArray("tasks");
        if (arr == null) return;

        String[] groups = {"doing", "todo", "blocked", "done"};
        int[] names = {R.string.sec_now, R.string.sec_next, R.string.sec_wait, R.string.sec_done};

        for (int g = 0; g < groups.length; g++) {
            List<BoardAdapter.Task> bucket = new ArrayList<>();
            for (int i = 0; i < arr.length(); i++) {
                JSONObject t = arr.optJSONObject(i);
                if (t == null) continue;
                String st = t.optString("Status", "todo");
                boolean match;
                if (g == 0) match = "doing".equals(st);
                else if (g == 1) match = "todo".equals(st);
                else if (g == 2) match = "blocked".equals(st) || "dropped".equals(st);
                else match = "done".equals(st);
                if (!match) continue;
                if (!activeSection.isEmpty() && !activeSection.equals(t.optString("Section", ""))) continue;
                BoardAdapter.Task mt = new BoardAdapter.Task();
                mt.id = t.optString("Id");
                mt.title = t.optString("Title");
                mt.note = t.optString("Note");
                mt.progress = t.optInt("Progress", 0);
                mt.chip = t.optString("Mode", "").toUpperCase(Locale.ROOT);
                bucket.add(mt);
            }
            if (bucket.isEmpty()) continue;
            rows.add(new BoardAdapter.Section(getString(names[g]), bucket.size()));
            bucket.sort((a, b2) -> Integer.compare(b2.progress, a.progress));
            rows.addAll(bucket);
        }
        adapter.setItems(rows);
        View empty = findViewById(R.id.empty);
        if (empty != null) empty.setVisibility(rows.isEmpty() ? View.VISIBLE : View.GONE);
    }

    private String unitLabel(String unit) {
        if (unit == null) return "";
        switch (unit) {
            case "шагов": return getString(R.string.unit_steps);
            case "эпох": return getString(R.string.unit_epochs);
            default: return unit;
        }
    }

    private void renderTrain(JSONObject o) {
        boolean alive = o.optBoolean("alive", false);
        trState.setText(alive ? R.string.train_alive : R.string.train_dead);
        trState.setTextColor(alive
                ? NxBackdrop.color(this, R.attr.nxPrimary)
                : warningColor());
        trRun.setText(o.optString("run", "—"));
        String stage = o.optString("stage", "");
        String unit = o.optString("unit", "");
        trStage.setText(stage.isEmpty() ? "—" : (stage + (unit.isEmpty() ? "" : " · " + unitLabel(unit))));

        int step = o.optInt("step", 0);
        int total = o.optInt("total", 0);
        int pct = o.optInt("pct", 0);
        trBar.setProgress(Math.max(0, Math.min(100, pct)));
        trStep.setText(step + " / " + total + "   ·   " + pct + "%");

        double loss = o.optDouble("loss", Double.NaN);
        trLoss.setText(Double.isNaN(loss) ? "—" : String.format(Locale.US, "%.4f", loss));
        trEta.setText(o.optString("remain", "—"));
        trRate.setText(o.optString("rate", "—").replace(" с/шаг", " " + getString(R.string.unit_per_step)));
        trSteps.setText(String.valueOf(step));

        JSONArray losses = o.optJSONArray("losses");
        List<Float> vals = new ArrayList<>();
        if (losses != null) {
            for (int i = 0; i < losses.length(); i++) {
                double v = losses.optDouble(i, Double.NaN);
                if (!Double.isNaN(v)) vals.add((float) v);
            }
        }
        if (vals.size() > 60) vals = vals.subList(vals.size() - 60, vals.size());
        trSpark.setData(vals);

        JSONArray chain = o.optJSONArray("chainLines");
        StringBuilder sb = new StringBuilder();
        if (chain != null) {
            int from = Math.max(0, chain.length() - 3);
            for (int i = from; i < chain.length(); i++) {
                if (sb.length() > 0) sb.append((char) 10);
                sb.append(chain.optString(i));
            }
        }
        trChain.setText(sb.length() == 0 ? "—" : sb.toString());
        java.util.ArrayList<String> lines = new java.util.ArrayList<>();
        if (chain != null) for (int i = Math.max(0, chain.length() - 8); i < chain.length(); i++) lines.add(chain.optString(i));
        StringBuilder tail = new StringBuilder();
        for (int i = lines.size() - 1; i >= 0; i--) {
            if (tail.length() > 0) tail.append((char) 10);
            tail.append(lines.get(i));
        }
        trTail.setText(tail.length() == 0 ? "—" : tail.toString());
    }

    // ---------- ключ ----------

    private EditText field(LinearLayout box, String label, String value, String hint) {
        int text = NxBackdrop.color(this, R.attr.nxText);
        int dim = NxBackdrop.color(this, R.attr.nxTextDim);
        TextView lb = new TextView(this);
        lb.setText(label);
        lb.setTextSize(11f);
        lb.setLetterSpacing(0.1f);
        lb.setTextColor(dim);
        android.widget.LinearLayout.LayoutParams lp = new android.widget.LinearLayout.LayoutParams(
                android.widget.LinearLayout.LayoutParams.MATCH_PARENT,
                android.widget.LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.topMargin = (int) (14 * getResources().getDisplayMetrics().density);
        box.addView(lb, lp);

        EditText e = new EditText(this);
        e.setSingleLine(true);
        e.setText(value);
        e.setHint(hint);
        e.setTextSize(14f);
        e.setTextColor(text);
        e.setHintTextColor(dim);
        box.addView(e);
        return e;
    }

    private void openSettings() {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        int pad = (int) (20 * getResources().getDisplayMetrics().density);
        box.setPadding(pad, pad / 3, pad, 0);

        EditText url = field(box, getString(R.string.f_server).toUpperCase(java.util.Locale.ROOT), prefs.base(), "https://board.nexus-your-heper.ru");
        EditText key = field(box, getString(R.string.f_key).toUpperCase(java.util.Locale.ROOT), prefs.key(), getString(R.string.f_key_hint));
        EditText board = field(box, getString(R.string.f_board).toUpperCase(java.util.Locale.ROOT), prefs.boardId(), getString(R.string.f_board_hint));

        TextView hint = new TextView(this);
        hint.setText(R.string.f_note);
        hint.setTextSize(12f);
        hint.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        android.widget.LinearLayout.LayoutParams hp = new android.widget.LinearLayout.LayoutParams(
                android.widget.LinearLayout.LayoutParams.MATCH_PARENT,
                android.widget.LinearLayout.LayoutParams.WRAP_CONTENT);
        hp.topMargin = (int) (14 * getResources().getDisplayMetrics().density);
        box.addView(hint, hp);

        new AlertDialog.Builder(this)
                .setTitle(R.string.key_title)
                .setView(box)
                .setPositiveButton(R.string.key_ok, (d, w) -> {
                    prefs.base(url.getText().toString().trim());
                    String k = key.getText().toString().trim();
                    prefs.key(k);
                    String bid = board.getText().toString().trim();
                    if (bid.matches("^[a-f0-9]{16}$")) prefs.boardId(bid);
                    if (k.isEmpty() && prefs.boardId().isEmpty()) registerBoard();
                    recreate();
                })
                .setNegativeButton(R.string.key_cancel, null)
                .show();
    }


    private boolean keyDialogShown = false;

    private void askKey() {
        if (keyDialogShown) return;
        keyDialogShown = true;
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        int pad = (int) (20 * getResources().getDisplayMetrics().density);
        box.setPadding(pad, pad / 2, pad, 0);

        TextView hint = new TextView(this);
        hint.setText(R.string.key_need);
        hint.setTextSize(13f);
        hint.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        box.addView(hint);

        EditText input = new EditText(this);
        input.setHint(R.string.key_hint);
        input.setSingleLine(true);
        input.setTextSize(16f);
        input.setTextColor(NxBackdrop.color(this, R.attr.nxText));
        input.setHintTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        input.setPadding(0, pad / 2, 0, 0);
        input.setText(prefs.key());
        box.addView(input);

        AlertDialog d = new AlertDialog.Builder(this)
                .setTitle(R.string.key_title)
                .setView(box)
                .setPositiveButton(R.string.key_ok, null)
                .setNegativeButton(R.string.key_cancel, null)
                .create();
        d.setOnDismissListener(x -> keyDialogShown = false);
        d.setOnShowListener(x -> d.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            String k = input.getText().toString().trim();
            if (k.isEmpty()) {
                Toast.makeText(this, R.string.key_need, Toast.LENGTH_SHORT).show();
                return;
            }
            prefs.key(k);
            d.dismiss();
            load();
        }));
        d.show();
    }

    private void editBase() {
        EditText input = new EditText(this);
        input.setSingleLine(true);
        input.setText(prefs.base());
        new AlertDialog.Builder(this)
                .setTitle("Сервер / Server")
                .setView(input)
                .setPositiveButton(R.string.key_ok, (dd, w) -> {
                    prefs.base(input.getText().toString().trim());
                    load();
                })
                .setNegativeButton(R.string.key_cancel, null)
                .show();
    }

    // ---------- обновления ----------

    private void checkUpdates() {
        checkUpdates(false);
    }

    private void checkUpdates(final boolean manual) {
        if (pendingUpdate != null) return;
        long last = prefs.lastUpdateCheck();
        if (!manual && System.currentTimeMillis() - last < 6L * 3600L * 1000L) return;
        if (manual) status.setText(R.string.loading);
        final String key = prefs.key();
        final String base = prefs.base();
        pool.execute(() -> {
            try {
                JSONObject o = Api.json(base, "/api/app/latest", key);
                prefs.lastUpdateCheck(System.currentTimeMillis());
                final int code = o.optInt("versionCode", 0);
                final String name = o.optString("versionName", "");
                final String file = o.optString("file", "");
                final String notes = o.optString("notes", "");
                if (code > BuildConfig.VERSION_CODE && !file.isEmpty()) {
                    ui.post(() -> {
                        pendingUpdate = file;
                        pendingNotes = notes;
                        status.setText(getString(R.string.update_available, name));
                        status.setTextColor(NxBackdrop.color(this, R.attr.nxPrimary));
                        status.setTypeface(Typeface.DEFAULT_BOLD);
                        status.setOnClickListener(v -> downloadUpdate());
                        findViewById(R.id.btn_update).setBackgroundResource(R.drawable.nx_pill_on);
                    });
                } else if (manual) {
                    ui.post(() -> Toast.makeText(this, R.string.update_none, Toast.LENGTH_SHORT).show());
                }
            } catch (Exception ignored) {
            }
        });
    }

    private void downloadUpdate() {
        if (pendingUpdate == null) return;
        final String file = pendingUpdate;
        final String key = prefs.key();
        final String base = prefs.base();
        status.setText(R.string.update_dl);
        status.setOnClickListener(null);
        pool.execute(() -> {
            try {
                byte[] data = Api.bytes(base + "/app/" + file, key);
                File out = new File(getCacheDir(), "update.apk");
                FileOutputStream fos = new FileOutputStream(out);
                fos.write(data);
                fos.close();
                ui.post(() -> install(out));
            } catch (Exception e) {
                ui.post(() -> {
                    status.setText(R.string.net_error);
                    pendingUpdate = null;
                });
            }
        });
    }

    private void install(File apk) {
        status.setText(R.string.update_ready);
        Uri uri = FileProvider.getUriForFile(this, getPackageName() + ".fileprovider", apk);
        Intent i = new Intent(Intent.ACTION_VIEW);
        i.setDataAndType(uri, "application/vnd.android.package-archive");
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        try {
            startActivity(i);
        } catch (Exception e) {
            Toast.makeText(this, R.string.net_error, Toast.LENGTH_SHORT).show();
        }
    }

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            load();
            ui.postDelayed(this, lastFailed ? REFRESH_SLOW_MS : REFRESH_MS);
        }
    };

    @Override
    protected void onResume() {
        super.onResume();
        load();
        ui.removeCallbacks(tick);
        ui.postDelayed(tick, REFRESH_MS);
    }

    @Override
    protected void onPause() {
        ui.removeCallbacks(tick);
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        pool.shutdownNow();
    }
}
