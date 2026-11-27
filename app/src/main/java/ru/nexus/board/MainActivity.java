package ru.nexus.board;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.res.Configuration;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.SeekBar;
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
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

public class MainActivity extends AppCompatActivity implements BoardAdapter.Listener {

    private static final long REFRESH_MS = 10000L;
    private static final long REFRESH_SLOW_MS = 30000L;
    private static final long VIEWS_MS = 5000L;
    private static final int TAB_BOARD = 0;
    private static final int TAB_TRAIN = 1;
    private static final int TAB_VIEWS = 2;

    private static final String[] STATUS_IDS = {"doing", "todo", "blocked", "done"};
    private static final String[] MODE_IDS = {"auto", "task", "night"};

    private Prefs prefs;
    private final ExecutorService pool = Executors.newFixedThreadPool(3);
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final AtomicBoolean boardRequest = new AtomicBoolean(false);
    private volatile boolean loading = false;
    private volatile boolean lastFailed = false;

    private BoardAdapter adapter;
    private RecyclerView list;
    private SwipeRefreshLayout swipe;
    private SwipeRefreshLayout viewsSwipe;
    private NestedScrollView trainScroll;
    private LinearLayout viewsBox;
    private LinearLayout chips;
    private HorizontalScrollView chipsWrap;
    private final List<String[]> sections = new ArrayList<>();
    private String activeSection = "";
    private JSONObject lastTasks;
    private TextView status, tabBoard, tabTrain, tabViews;
    private TextView trState, trRun, trStage, trStep, trLoss, trEta, trRate, trSteps, trChain, trTail;
    private View trLogCard;
    private ProgressBar trBar;
    private SparkView trSpark;
    private AlertDialog openDialog;

    private int tab = TAB_BOARD;
    private volatile boolean viewsLoading = false;
    private boolean viewsReady = false;
    private String viewsVersion = "";
    private String viewsSig = "";
    private int viewsCount = -1;

    private String pendingUpdate = "";
    private String pendingName = "";
    private String pendingNotes = "";
    private String pendingSha = "";
    private int pendingCode;
    private File pendingApk;

    private interface Pick {
        void on(int index);
    }

    @Override
    protected void onCreate(Bundle b) {
        prefs = new Prefs(this);
        applyTheme();
        super.onCreate(b);
        setContentView(R.layout.activity_main);

        NxBackdrop.apply(findViewById(R.id.root));

        pendingUpdate = prefs.updFile();
        pendingName = prefs.updName();
        pendingNotes = prefs.updNotes();
        pendingSha = prefs.updSha();
        pendingCode = prefs.updCode();
        // After an install the old process is gone, so any persisted offer for
        // this or an older build is stale. Unknown code (migration from an
        // older app) is treated as stale too.
        if (!pendingUpdate.isEmpty() && pendingCode <= BuildConfig.VERSION_CODE) {
            clearPendingUpdate();
        }

        status = findViewById(R.id.status);
        tabBoard = findViewById(R.id.tab_board);
        tabTrain = findViewById(R.id.tab_train);
        tabViews = findViewById(R.id.tab_views);
        list = findViewById(R.id.list);
        swipe = findViewById(R.id.swipe);
        viewsSwipe = findViewById(R.id.views_swipe);
        viewsBox = findViewById(R.id.views_box);
        trainScroll = findViewById(R.id.train_scroll);
        chips = findViewById(R.id.chips);
        chipsWrap = findViewById(R.id.chips_wrap);
        trLogCard = findViewById(R.id.tr_log_card);

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
        adapter.setListener(this);
        list.setLayoutManager(new LinearLayoutManager(this));
        list.setAdapter(adapter);
        list.setOnLongClickListener(v -> {
            openTaskDialog(null);
            return true;
        });

        int accent = NxBackdrop.color(this, R.attr.nxPrimary);
        swipe.setColorSchemeColors(accent);
        swipe.setProgressBackgroundColorSchemeColor(NxBackdrop.color(this, R.attr.nxSurface));
        swipe.setOnRefreshListener(this::load);
        viewsSwipe.setColorSchemeColors(accent);
        viewsSwipe.setProgressBackgroundColorSchemeColor(NxBackdrop.color(this, R.attr.nxSurface));
        viewsSwipe.setOnRefreshListener(() -> loadViews(true));

        findViewById(R.id.btn_theme).setOnClickListener(v -> nextTheme());
        findViewById(R.id.btn_chat).setOnClickListener(v ->
                startActivity(new Intent(MainActivity.this, ChatActivity.class)));
        findViewById(R.id.btn_lang).setOnClickListener(v -> switchLang());
        findViewById(R.id.btn_key).setOnClickListener(v -> openConnect());
        findViewById(R.id.btn_update).setOnClickListener(v -> {
            if (!pendingUpdate.isEmpty()) downloadUpdate();
            else checkUpdates(true);
        });

        TextView langBtn = findViewById(R.id.btn_lang);
        langBtn.setText("ru".equals(prefs.lang()) ? "EN" : "RU");

        tabBoard.setOnClickListener(v -> selectTab(TAB_BOARD));
        tabTrain.setOnClickListener(v -> selectTab(TAB_TRAIN));
        tabViews.setOnClickListener(v -> selectTab(TAB_VIEWS));
        updateTabs();
        int start = prefs.tabMode();
        boolean allowed = prefs.nexusMode() || !prefs.boardId().isEmpty();
        if (!allowed) start = TAB_BOARD;
        if (start == TAB_TRAIN && !prefs.nexusMode()) start = TAB_BOARD;
        if (start != TAB_TRAIN && start != TAB_VIEWS) start = TAB_BOARD;
        if (start == TAB_VIEWS && !allowed) start = TAB_BOARD;
        selectTab(start);
        findViewById(R.id.empty_btn).setOnClickListener(v -> openConnect());

        View empty = findViewById(R.id.empty);
        empty.setOnLongClickListener(v -> {
            openTaskDialog(null);
            return true;
        });

        status.setOnClickListener(v -> {
            if (!pendingUpdate.isEmpty()) openUpdateDialog();
            else openConnect();
        });
        status.setOnLongClickListener(v -> {
            openConnect();
            return true;
        });

        findViewById(R.id.title).setOnLongClickListener(v -> {
            openSettings();
            return true;
        });

        if (!pendingUpdate.isEmpty()) showUpdateStatus();
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

    private boolean alive() {
        return !isFinishing() && !isDestroyed();
    }

    private void uiPost(Runnable r) {
        ui.post(() -> {
            if (!alive()) return;
            r.run();
        });
    }

    private int dp(float v) {
        return (int) (v * getResources().getDisplayMetrics().density);
    }

    private void toast(int res) {
        if (alive()) Toast.makeText(this, res, Toast.LENGTH_SHORT).show();
    }

    private void toast(String text) {
        if (alive()) Toast.makeText(this, text, Toast.LENGTH_SHORT).show();
    }

    private void reportFailure(int code) {
        if (code == 401 || code == 403) {
            askKey();
        } else if (code == -1) {
            toast(R.string.net_error);
        } else if (code == 404) {
            toast(R.string.delete_unsupported);
        } else {
            toast(getString(R.string.server_reply, String.valueOf(code)));
        }
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

    private void selectTab(int next) {
        tab = next;
        prefs.tabMode(next);
        trainScroll.setVisibility(next == TAB_TRAIN ? View.VISIBLE : View.GONE);
        swipe.setVisibility(next == TAB_BOARD ? View.VISIBLE : View.GONE);
        viewsSwipe.setVisibility(next == TAB_VIEWS ? View.VISIBLE : View.GONE);
        int onText = NxBackdrop.color(this, R.attr.nxOnPrimary);
        int dim = NxBackdrop.color(this, R.attr.nxTextDim);
        pill(tabBoard, next == TAB_BOARD, onText, dim);
        pill(tabTrain, next == TAB_TRAIN, onText, dim);
        pill(tabViews, next == TAB_VIEWS, onText, dim);
        if (next == TAB_BOARD) {
            renderTasks(lastTasks);
        } else {
            View empty = findViewById(R.id.empty);
            if (empty != null) empty.setVisibility(View.GONE);
        }
        if (next == TAB_VIEWS) {
            loadViews(true);
            ui.removeCallbacks(viewsTick);
            ui.postDelayed(viewsTick, VIEWS_MS);
        } else {
            ui.removeCallbacks(viewsTick);
        }
    }

    private void pill(TextView tv, boolean active, int onText, int dim) {
        tv.setBackgroundResource(active ? R.drawable.nx_pill_on : R.drawable.nx_pill_off);
        tv.setTextColor(active ? onText : dim);
    }

    private void updateTabs() {
        boolean nexus = prefs.nexusMode() && !prefs.personal();
        boolean board = !prefs.boardId().isEmpty();
        findViewById(R.id.tabs).setVisibility(nexus || board ? View.VISIBLE : View.GONE);
        tabTrain.setVisibility(nexus ? View.VISIBLE : View.GONE);
        tabViews.setVisibility(nexus || board ? View.VISIBLE : View.GONE);
    }

    private final Runnable viewsTick = new Runnable() {
        @Override
        public void run() {
            if (!alive()) return;
            if (tab == TAB_VIEWS) loadViews(false);
            ui.postDelayed(this, VIEWS_MS);
        }
    };

    private void loadViews(boolean force) {
        final String key = prefs.key();
        final String base = prefs.base();
        final String board = prefs.boardId();
        if (force) viewsVersion = "";
        if (viewsLoading) return;
        if (board.isEmpty()) {
            viewsSwipe.setRefreshing(false);
            renderViewsHint();
            return;
        }
        viewsLoading = true;
        if (force) viewsSwipe.setRefreshing(true);
        if (force) viewsSig = "";
        pool.execute(() -> {
            JSONObject o = null;
            try {
                o = Api.views(base, board, key);
            } catch (Exception ignored) {
            }
            final JSONObject fo = o;
            uiPost(() -> {
                viewsLoading = false;
                viewsSwipe.setRefreshing(false);
                if (fo != null) renderViews(fo);
                else if (!viewsReady) renderViewsHint();
            });
        });
    }

    private View hintCard(int titleRes, int bodyRes) {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        int p = dp(16);
        card.setPadding(p, p, p, p);
        card.setBackgroundResource(R.drawable.nx_card);

        TextView title = new TextView(this);
        title.setText(titleRes);
        title.setTextSize(16f);
        title.setTextColor(NxBackdrop.color(this, R.attr.nxText));
        title.setTypeface(Typeface.DEFAULT_BOLD);
        card.addView(title);

        TextView body = new TextView(this);
        body.setText(bodyRes);
        body.setTextSize(13f);
        body.setLineSpacing(dp(3), 1f);
        body.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.topMargin = dp(8);
        card.addView(body, lp);
        return card;
    }

    private void renderViewsHint() {
        viewsReady = true;
        viewsVersion = "";
        viewsSig = "";
        viewsCount = -1;
        viewsBox.removeAllViews();
        boolean noBoard = prefs.boardId().isEmpty();
        viewsBox.addView(hintCard(noBoard ? R.string.views_need_board : R.string.views_empty_title,
                noBoard ? R.string.views_need_body : R.string.views_empty_body));
    }

    private String tasksSig() {
        if (lastTasks == null) return "-";
        JSONArray arr = lastTasks.optJSONArray("tasks");
        int doing = 0, done = 0;
        if (arr != null) {
            for (int i = 0; i < arr.length(); i++) {
                JSONObject t = arr.optJSONObject(i);
                if (t == null) continue;
                String st = t.optString("Status", "todo");
                if ("doing".equals(st)) doing++;
                else if ("done".equals(st)) done++;
            }
        }
        return (arr == null ? 0 : arr.length()) + "/" + doing + "/" + done;
    }

    private void renderViews(JSONObject o) {
        JSONArray blocks = o.optJSONArray("blocks");
        List<JSONObject> items = new ArrayList<>();
        if (blocks != null) {
            for (int i = 0; i < blocks.length(); i++) {
                JSONObject b = blocks.optJSONObject(i);
                if (b != null) items.add(b);
            }
        }
        items.sort((a, b) -> Integer.compare(a.optInt("order", 0), b.optInt("order", 0)));
        String version = o.optString("version", "");
        String sig = tasksSig();
        if (viewsReady && !version.isEmpty() && version.equals(viewsVersion)
                && items.size() == viewsCount && sig.equals(viewsSig)) {
            return;
        }
        viewsReady = true;
        viewsVersion = version;
        viewsSig = sig;
        viewsCount = items.size();
        viewsBox.removeAllViews();

        BlocksView render = new BlocksView(this, lastTasks, prefs.boardId());
        for (JSONObject b : items) {
            View v = render.build(b);
            if (v != null) viewsBox.addView(v);
        }
        if (viewsBox.getChildCount() == 0) {
            viewsBox.addView(hintCard(R.string.views_empty_title, R.string.views_empty_body));
        }
    }

    private void load() {
        final String key = prefs.key();
        final String base = prefs.base();
        final String board = prefs.boardId();
        final boolean hasBoard = Prefs.validBoardId(board);
        final boolean nexus = !key.isEmpty() && !prefs.personal();
        if (!hasBoard) {
            status.setText(R.string.connecting);
            ensureBoardAsync();
            return;
        }
        if (loading) return;
        loading = true;
        status.setText(R.string.loading);
        swipe.setRefreshing(true);
        pool.execute(() -> {
            JSONObject tasks = null;
            JSONObject train = null;
            String error = null;
            boolean auth = false;
            boolean ownerTasks = nexus;
            String boardPath = "/api/b/tasks?b=" + board
                    + (key.isEmpty() ? "" : "&k=" + Uri.encode(key));
            try {
                // Tasks are edited through /api/b/add|update|delete, so they
                // must also be read from the same board file. /api/tasks is
                // the legacy global file and used to show different tasks.
                tasks = hasBoard
                        ? Api.json(base, boardPath, "")
                        : Api.json(base, "/api/tasks", key);
            } catch (Api.AuthException e) {
                auth = true;
            } catch (Exception e) {
                error = e.getMessage();
            }
            if (ownerTasks) {
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
            uiPost(() -> {
                loading = false;
                lastFailed = fError != null && fTasks == null && fTrain == null;
                swipe.setRefreshing(false);
                updateTabs();
                if (fAuth && fTasks == null) {
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
                status.setOnClickListener(v -> {
                    if (!pendingUpdate.isEmpty()) openUpdateDialog();
                    else openConnect();
                });
                if (pendingUpdate.isEmpty()) status.setText(getString(R.string.updated, now()));
                else showUpdateStatus();
                checkUpdates();
            });
        });
    }

    private void ensureBoardAsync() {
        if (!boardRequest.compareAndSet(false, true)) return;
        pool.execute(() -> {
            final String id = Prefs.ensureBoard(MainActivity.this);
            boardRequest.set(false);
            uiPost(() -> {
                if (!id.isEmpty()) {
                    load();
                    return;
                }
                status.setText(R.string.net_error_tap);
                status.setTextColor(warningColor());
                status.setTypeface(Typeface.DEFAULT_BOLD);
                status.setOnClickListener(v -> load());
            });
        });
    }

    private int warningColor() {
        return "frost".equals(prefs.theme()) ? 0xFFB45309 : 0xFFF0B429;
    }

    private String now() {
        return new SimpleDateFormat("HH:mm", Locale.getDefault()).format(new Date());
    }

    // ---------- ключ доски ----------

    private EditText makeInput(String value, String hint, boolean multiline) {
        EditText e = new EditText(this);
        e.setTextSize(15f);
        e.setTextColor(NxBackdrop.color(this, R.attr.nxText));
        e.setHintTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        e.setBackgroundResource(R.drawable.nx_editor);
        int p = dp(10);
        e.setPadding(p, p, p, p);
        if (multiline) {
            e.setSingleLine(false);
            e.setInputType(InputType.TYPE_CLASS_TEXT
                    | InputType.TYPE_TEXT_FLAG_MULTI_LINE
                    | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
            e.setGravity(Gravity.TOP | Gravity.START);
            e.setMinLines(3);
            e.setMaxLines(9);
        } else {
            e.setSingleLine(true);
            e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        }
        e.setHint(hint);
        e.setText(value);
        return e;
    }

    private TextView smallLabel(String text) {
        TextView tv = new TextView(this);
        tv.setText(text);
        tv.setTextSize(10.5f);
        tv.setLetterSpacing(0.14f);
        tv.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        return tv;
    }

    private LinearLayout fieldBox(LinearLayout box, int labelRes, View input) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.topMargin = dp(12);
        box.addView(smallLabel(getString(labelRes)), lp);
        box.addView(input);
        return box;
    }

    /** Код для глаза и диктовки: 7140-A973-CFE1-946D. */
    private static String formatBoardCode(String raw) {
        String v = raw == null ? "" : raw.trim().toLowerCase(Locale.ROOT);
        if (v.length() != 16) return v;
        StringBuilder sb = new StringBuilder(19);
        for (int i = 0; i < v.length(); i++) {
            if (i > 0 && i % 4 == 0) sb.append('-');
            sb.append(Character.toUpperCase(v.charAt(i)));
        }
        return sb.toString();
    }

    private void openConnect() {
        final EditText input = makeInput(formatBoardCode(prefs.boardId()), "7140-A973-CFE1-946D", false);
        input.setInputType(InputType.TYPE_CLASS_TEXT
                | InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD
                | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        input.setTypeface(Typeface.MONOSPACE);

        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(4), dp(20), 0);
        box.addView(input);

        final TextView preview = new TextView(this);
        preview.setTextSize(11.5f);
        preview.setTypeface(Typeface.MONOSPACE);
        preview.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        LinearLayout.LayoutParams vlp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        vlp.topMargin = dp(8);
        box.addView(preview, vlp);
        final Runnable showCode = () -> {
            String raw = cleanKey(input.getText().toString());
            if (raw.isEmpty()) {
                preview.setText(getString(R.string.code_example));
            } else if (raw.length() < 16) {
                preview.setText(formatBoardCode(raw) + "  " + raw.length() + "/16");
            } else {
                preview.setText(formatBoardCode(raw) + "  ✓");
            }
        };
        input.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) { }
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) { }
            @Override public void afterTextChanged(Editable s) { showCode.run(); }
        });
        showCode.run();

        TextView paste = new TextView(this);
        paste.setText(getString(R.string.paste_key));
        paste.setTextSize(13f);
        paste.setGravity(Gravity.CENTER);
        paste.setPadding(dp(14), dp(10), dp(14), dp(10));
        paste.setBackgroundResource(R.drawable.nx_chip);
        paste.setTextColor(NxBackdrop.color(this, R.attr.nxText));
        LinearLayout.LayoutParams plp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        plp.topMargin = dp(10);
        box.addView(paste, plp);
        paste.setOnClickListener(v -> {
            ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
            if (cm == null || !cm.hasPrimaryClip() || cm.getPrimaryClip() == null
                    || cm.getPrimaryClip().getItemCount() == 0) {
                toast(R.string.key_empty_clip);
                return;
            }
            CharSequence raw = cm.getPrimaryClip().getItemAt(0).coerceToText(this);
            // Keep the whole link: applyKey() reads both b= and k= from it.
            input.setText(String.valueOf(raw).trim());
            input.setSelection(input.getText().length());
            if (input.getText().length() > 0) input.setSelection(input.getText().length());
        });

        AlertDialog dlg = new AlertDialog.Builder(this)
                .setTitle(R.string.connect_title)
                .setMessage(R.string.connect_hint)
                .setView(box)
                .setPositiveButton(R.string.apply_key, null)
                .setNeutralButton(R.string.copy_key, null)
                .setNegativeButton(R.string.close, null)
                .create();
        dlg.setOnShowListener(x -> {
            dlg.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
                if (applyKey(input.getText().toString())) dlg.dismiss();
            });
            dlg.getButton(AlertDialog.BUTTON_NEUTRAL).setOnClickListener(v -> {
                String key = cleanKey(input.getText().toString());
                if (key.isEmpty()) {
                    toast(R.string.copy_empty);
                    return;
                }
                ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                if (cm == null) return;
                cm.setPrimaryClip(ClipData.newPlainText("Nexus Board key", key));
                toast(R.string.key_copied);
            });
        });
        showSafe(dlg);
    }

    private static String cleanKey(String raw) {
        String v = raw == null ? "" : raw.trim().toLowerCase(Locale.ROOT);
        // Board links use ?b=<board id>&k=<key>; b= must win, otherwise the
        // first 16 hex chars of a site token could be mistaken for a board.
        int at = v.indexOf("b=");
        if (at >= 0) {
            v = v.substring(at + 2);
            int end = v.length();
            for (char stop : new char[] { '&', '#', ' ', '\n', '\r' }) {
                int p = v.indexOf(stop);
                if (p >= 0 && p < end) end = p;
            }
            v = v.substring(0, end);
        } else {
            at = v.indexOf("k=");
            if (at >= 0) v = v.substring(at + 2);
        }
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < v.length() && sb.length() < 16; i++) {
            char c = v.charAt(i);
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) sb.append(c);
        }
        return sb.toString();
    }

    /** Reads b= or k= from a share link, stopping at the next parameter. */
    private static String linkParam(String text, String name) {
        int at = text.indexOf(name);
        if (at < 0) return "";
        String v = text.substring(at + name.length());
        int end = v.length();
        for (char stop : new char[] { '&', '#', ' ', '\n', '\r' }) {
            int p = v.indexOf(stop);
            if (p >= 0 && p < end) end = p;
        }
        return v.substring(0, end).trim();
    }

    private boolean applyKey(String raw) {
        String v = raw == null ? "" : raw.trim().toLowerCase(Locale.ROOT);
        // A share link carries both ids. The old code kept only b= and left
        // the key from another board, so a protected board opened as 401.
        String board = linkParam(v, "b=");
        String key = linkParam(v, "k=");
        if (Prefs.validBoardId(board)) {
            prefs.boardId(board);
            if (!key.isEmpty()) prefs.key(key);
            prefs.personal(false);
            resetBoardState();
            toast(R.string.key_ok);
            load();
            return true;
        }
        String single = cleanKey(v);
        if (single.isEmpty()) {
            newBoard();
            return true;
        }
        if (single.length() != 16) {
            toast(R.string.key_bad);
            return false;
        }
        prefs.boardId(single);
        prefs.personal(false);
        resetBoardState();
        toast(R.string.key_ok);
        load();
        return true;
    }

    private void newBoard() {
        prefs.clearBoard();
        prefs.personal(false);
        resetBoardState();
        toast(R.string.board_new);
        ensureBoardAsync();
    }

    private void resetBoardState() {
        lastTasks = null;
        sections.clear();
        activeSection = "";
        viewsReady = false;
        viewsVersion = "";
        viewsSig = "";
        viewsCount = -1;
        if (adapter != null) adapter.setItems(new ArrayList<>());
        renderChips();
        updateTabs();
    }

    private void openSettings() {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(8), dp(20), 0);

        final EditText url = makeInput(prefs.base(), "https://board.nexus-your-heper.ru", false);
        final EditText key = makeInput(prefs.key(), getString(R.string.f_key_hint), false);
        final EditText board = makeInput(prefs.boardId(), getString(R.string.f_board_hint), false);
        fieldBox(box, R.string.f_server, url);
        fieldBox(box, R.string.f_key, key);
        fieldBox(box, R.string.f_board, board);

        TextView hint = new TextView(this);
        hint.setText(R.string.f_note);
        hint.setTextSize(12f);
        hint.setLineSpacing(dp(3), 1f);
        hint.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        LinearLayout.LayoutParams hp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        hp.topMargin = dp(14);
        box.addView(hint, hp);

        AlertDialog dlg = new AlertDialog.Builder(this)
                .setTitle(R.string.key_title)
                .setView(box)
                .setPositiveButton(R.string.key_ok, null)
                .setNegativeButton(R.string.key_cancel, null)
                .create();
        dlg.setOnShowListener(x -> dlg.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            String bid = board.getText().toString().trim().toLowerCase(Locale.ROOT);
            if (!bid.isEmpty() && !bid.matches("^[a-f0-9]{16}$")) {
                toast(R.string.board_bad);
                return;
            }
            String rawKey = key.getText().toString().trim();
            prefs.base(url.getText().toString().trim());
            prefs.key(rawKey.matches("(?i)^[a-f0-9]{16}$") ? rawKey.toLowerCase(Locale.ROOT) : rawKey);
            boolean fresh = bid.isEmpty();
            if (fresh) prefs.clearBoard();
            else prefs.boardId(bid);
            prefs.personal(false);
            lastTasks = null;
            sections.clear();
            activeSection = "";
            dlg.dismiss();
            if (fresh) toast(R.string.board_new);
            recreate();
        }));
        showSafe(dlg);
    }

    private void showSafe(AlertDialog d) {
        if (!alive()) return;
        openDialog = d;
        d.show();
    }

    private boolean keyDialogShown = false;

    private void askKey() {
        if (keyDialogShown || !alive()) return;
        keyDialogShown = true;
        final EditText input = makeInput(prefs.key(), getString(R.string.key_hint), false);

        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(4), dp(20), 0);

        TextView hint = new TextView(this);
        hint.setText(R.string.key_need);
        hint.setTextSize(13f);
        hint.setTextColor(NxBackdrop.color(this, R.attr.nxTextDim));
        box.addView(hint);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.topMargin = dp(10);
        box.addView(input, lp);

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
                toast(R.string.key_need);
                return;
            }
            prefs.key(k);
            d.dismiss();
            load();
        }));
        showSafe(d);
    }

    // ---------- задачи ----------

    @Override
    public void onTaskClick(BoardAdapter.Task t) {
        openTaskFull(t);
    }

    @Override
    public void onTaskLongClick(BoardAdapter.Task t) {
        openTaskDialog(t);
    }

    private View bounded(View content, float ratio) {
        FrameLayout holder = new FrameLayout(this);
        int h = (int) (getResources().getDisplayMetrics().heightPixels * ratio);
        holder.addView(content, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, h));
        return holder;
    }

    private void openTaskFull(final BoardAdapter.Task t) {
        if (t == null || !alive()) return;
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(6), dp(20), dp(6));

        TextView title = new TextView(this);
        title.setText(t.title);
        title.setTextSize(18f);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextColor(NxBackdrop.color(this, R.attr.nxText));
        title.setTextIsSelectable(true);
        box.addView(title);

        TextView meta = new TextView(this);
        String mode = t.mode == null || t.mode.isEmpty() ? "task" : t.mode;
        meta.setText(getString(R.string.task_meta, statusLabel(t.status), mode.toUpperCase(Locale.ROOT), t.progress));
        meta.setTextSize(11.5f);
        meta.setTextColor(NxBackdrop.color(this, R.attr.nxPrimary));
        LinearLayout.LayoutParams mlp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        mlp.topMargin = dp(6);
        box.addView(meta, mlp);

        TextView note = new TextView(this);
        String text = t.note == null ? "" : t.note.trim();
        note.setText(text.isEmpty() ? getString(R.string.task_no_note) : text);
        note.setTextSize(14.5f);
        note.setLineSpacing(dp(3), 1f);
        note.setTextIsSelectable(true);
        note.setTextColor(NxBackdrop.color(this, text.isEmpty() ? R.attr.nxTextDim : R.attr.nxText));
        LinearLayout.LayoutParams nlp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        nlp.topMargin = dp(12);
        box.addView(note, nlp);

        NestedScrollView scroller = new NestedScrollView(this);
        scroller.addView(box);

        AlertDialog dlg = new AlertDialog.Builder(this)
                .setTitle(R.string.task_full_title)
                .setView(bounded(scroller, 0.62f))
                .setPositiveButton(R.string.close, null)
                .setNeutralButton(R.string.edit, (d, w) -> openTaskDialog(t))
                .create();
        showSafe(dlg);
    }

    private String statusLabel(String status) {
        if ("doing".equals(status)) return getString(R.string.sec_now);
        if ("blocked".equals(status) || "dropped".equals(status)) return getString(R.string.sec_wait);
        if ("done".equals(status)) return getString(R.string.sec_done);
        return getString(R.string.sec_next);
    }

    private int statusIndex(String status) {
        for (int i = 0; i < STATUS_IDS.length; i++) {
            if (STATUS_IDS[i].equals(status)) return i;
        }
        return 1;
    }

    private void stylePick(TextView tv, boolean active) {
        tv.setBackgroundResource(active ? R.drawable.nx_pill_on : R.drawable.nx_chip);
        tv.setTextColor(NxBackdrop.color(this, active ? R.attr.nxOnPrimary : R.attr.nxText));
    }

    private LinearLayout pickRow(LinearLayout box, int labelRes, String[] labels,
                                 final int[] sel, final Pick cb) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        final TextView[] pills = new TextView[labels.length];
        for (int i = 0; i < labels.length; i++) {
            final int idx = i;
            TextView tv = new TextView(this);
            tv.setText(labels[i]);
            tv.setTextSize(11.5f);
            tv.setGravity(Gravity.CENTER);
            tv.setPadding(dp(6), dp(9), dp(6), dp(9));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f);
            if (i > 0) lp.setMarginStart(dp(6));
            row.addView(tv, lp);
            pills[i] = tv;
            tv.setOnClickListener(v -> {
                sel[0] = idx;
                for (int j = 0; j < pills.length; j++) stylePick(pills[j], j == sel[0]);
                if (cb != null) cb.on(idx);
            });
        }
        for (int i = 0; i < pills.length; i++) stylePick(pills[i], i == sel[0]);

        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.topMargin = dp(6);
        box.addView(smallLabel(getString(labelRes)), new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        box.addView(row, lp);
        return row;
    }

    private void openTaskDialog(final BoardAdapter.Task task) {
        if (!alive()) return;
        final boolean edit = task != null;

        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(4), dp(20), 0);

        final EditText title = makeInput(edit ? task.title : "", getString(R.string.f_task_title_hint), false);
        final EditText note = makeInput(edit && task.note != null ? task.note : "",
                getString(R.string.f_task_note_hint), true);
        fieldBox(box, R.string.f_task_title, title);
        fieldBox(box, R.string.f_task_note, note);

        final List<String> secIds = new ArrayList<>();
        final List<String> secNames = new ArrayList<>();
        secIds.add("");
        secNames.add(getString(R.string.no_section));
        for (String[] s : sections) {
            secIds.add(s[0]);
            secNames.add(s[1]);
        }
        final int[] secSel = {0};
        final String current = edit && task.section != null ? task.section : "";
        for (int i = 1; i < secIds.size(); i++) {
            if (secIds.get(i).equals(current)) secSel[0] = i;
        }

        LinearLayout secRow = new LinearLayout(this);
        secRow.setOrientation(LinearLayout.HORIZONTAL);
        final TextView secBtn = new TextView(this);
        secBtn.setTextSize(12.5f);
        secBtn.setPadding(dp(14), dp(9), dp(14), dp(9));
        secBtn.setText(secNames.get(secSel[0]));
        secBtn.setBackgroundResource(R.drawable.nx_chip);
        secBtn.setTextColor(NxBackdrop.color(this, R.attr.nxText));
        secBtn.setOnClickListener(v -> {
            String[] names = secNames.toArray(new String[0]);
            showSafe(new AlertDialog.Builder(this)
                    .setTitle(R.string.f_task_section)
                    .setSingleChoiceItems(names, secSel[0], (dd, w) -> {
                        secSel[0] = w;
                        secBtn.setText(secNames.get(w));
                        dd.dismiss();
                    })
                    .setNegativeButton(R.string.key_cancel, null)
                    .create());
        });
        secRow.addView(secBtn);
        LinearLayout.LayoutParams srp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        srp.topMargin = dp(6);
        box.addView(smallLabel(getString(R.string.f_task_section)), new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        box.addView(secRow, srp);

        final int[] stSel = {edit ? statusIndex(task.status) : 1};
        String[] stLabels = {
                getString(R.string.sec_now),
                getString(R.string.sec_next),
                getString(R.string.sec_wait),
                getString(R.string.sec_done)};
        pickRow(box, R.string.f_task_status, stLabels, stSel, null);

        final int[] modeSel = {1};
        if (edit && task.mode != null) {
            for (int i = 0; i < MODE_IDS.length; i++) {
                if (MODE_IDS[i].equals(task.mode)) modeSel[0] = i;
            }
        }
        String[] modeLabels = {"AUTO", "TASK", "NIGHT"};
        pickRow(box, R.string.f_task_mode, modeLabels, modeSel, null);

        final int[] prog = {edit ? Math.max(0, Math.min(100, task.progress)) : 0};
        final TextView pct = new TextView(this);
        pct.setTextSize(13f);
        pct.setTextColor(NxBackdrop.color(this, R.attr.nxPrimary));
        pct.setTypeface(Typeface.DEFAULT_BOLD);
        pct.setText(getString(R.string.pct_value, prog[0]));
        SeekBar bar = new SeekBar(this);
        bar.setMax(100);
        bar.setProgress(prog[0]);
        bar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override
            public void onProgressChanged(SeekBar sb, int value, boolean fromUser) {
                prog[0] = value;
                pct.setText(getString(R.string.pct_value, value));
            }

            @Override
            public void onStartTrackingTouch(SeekBar sb) {
            }

            @Override
            public void onStopTrackingTouch(SeekBar sb) {
            }
        });
        LinearLayout progHead = new LinearLayout(this);
        progHead.setOrientation(LinearLayout.HORIZONTAL);
        progHead.setGravity(Gravity.CENTER_VERTICAL);
        progHead.addView(smallLabel(getString(R.string.f_task_progress)),
                new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        progHead.addView(pct);
        LinearLayout.LayoutParams phlp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        phlp.topMargin = dp(14);
        box.addView(progHead, phlp);
        box.addView(bar);

        NestedScrollView scroller = new NestedScrollView(this);
        scroller.addView(box);

        AlertDialog.Builder b = new AlertDialog.Builder(this)
                .setTitle(edit ? R.string.task_edit : R.string.task_new)
                .setView(bounded(scroller, 0.74f))
                .setPositiveButton(R.string.save, null)
                .setNegativeButton(R.string.key_cancel, null);
        if (edit) b.setNeutralButton(R.string.delete, null);
        final AlertDialog dlg = b.create();
        dlg.setOnShowListener(x -> {
            dlg.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
                String t = title.getText().toString().trim();
                if (t.isEmpty()) {
                    toast(R.string.task_need_title);
                    return;
                }
                JSONObject body = new JSONObject();
                try {
                    body.put("title", t);
                    body.put("note", note.getText().toString());
                    body.put("status", STATUS_IDS[stSel[0]]);
                    body.put("mode", MODE_IDS[modeSel[0]]);
                    body.put("progress", prog[0]);
                    body.put("section", secIds.get(secSel[0]));
                    if (edit) body.put("id", task.id);
                } catch (Exception ignored) {
                }
                dlg.dismiss();
                saveTask(edit, body);
            });
            if (edit) {
                dlg.getButton(AlertDialog.BUTTON_NEUTRAL).setOnClickListener(v -> {
                    dlg.dismiss();
                    deleteTask(task.id);
                });
            }
        });
        showSafe(dlg);
    }

    private void saveTask(final boolean edit, final JSONObject body) {
        final String board = prefs.boardId();
        final String base = prefs.base();
        final String key = prefs.key();
        if (board.isEmpty()) {
            toast(R.string.need_board);
            return;
        }
        final String path = edit ? "/api/b/update" : "/api/b/add";
        pool.execute(() -> {
            int code = -1;
            try {
                code = Api.post(base + path + "?b=" + board, key, body.toString()).status;
            } catch (Api.AuthException e) {
                code = 401;
            } catch (Exception ignored) {
            }
            final int f = code;
            uiPost(() -> {
                if (f == 200) {
                    toast(edit ? R.string.task_saved : R.string.task_added);
                    load();
                } else if (f == 404) {
                    toast(R.string.task_gone);
                    load();
                } else {
                    reportFailure(f);
                }
            });
        });
    }

    private void deleteTask(final String id) {
        final String board = prefs.boardId();
        final String base = prefs.base();
        final String key = prefs.key();
        if (board.isEmpty() || id == null || id.isEmpty()) return;
        pool.execute(() -> {
            int code = -1;
            try {
                JSONObject body = new JSONObject();
                body.put("id", id);
                code = Api.post(base + "/api/b/delete?b=" + board, key, body.toString()).status;
            } catch (Api.AuthException e) {
                code = 401;
            } catch (Exception ignored) {
            }
            final int f = code;
            uiPost(() -> {
                if (f == 200) {
                    toast(R.string.task_deleted);
                    load();
                } else if (f == 404) {
                    toast(R.string.delete_unsupported);
                } else {
                    reportFailure(f);
                }
            });
        });
    }

    // ---------- разделы ----------

    private void openSectionMenu(String sectionId) {
        final boolean all = sectionId == null || sectionId.isEmpty();
        final List<String> actions = new ArrayList<>();
        actions.add(getString(R.string.task_new));
        if (all) {
            actions.add(getString(R.string.section_new));
        } else {
            actions.add(getString(R.string.section_rename));
            actions.add(getString(R.string.section_delete));
        }
        showSafe(new AlertDialog.Builder(this)
                .setTitle(all ? R.string.section_actions_all : R.string.section_actions)
                .setItems(actions.toArray(new String[0]), (dd, w) -> {
                    if (w == 0) {
                        openTaskDialog(null);
                    } else if (all) {
                        openSectionCreate();
                    } else if (w == 1) {
                        openSectionRename(sectionId);
                    } else {
                        confirmSectionDelete(sectionId);
                    }
                })
                .setNegativeButton(R.string.key_cancel, null)
                .create());
    }

    private void openSectionCreate() {
        final EditText name = makeInput("", getString(R.string.section_name_hint), false);
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(6), dp(20), 0);
        box.addView(name);
        final AlertDialog dlg = new AlertDialog.Builder(this)
                .setTitle(R.string.section_new)
                .setView(box)
                .setPositiveButton(R.string.save, null)
                .setNegativeButton(R.string.key_cancel, null)
                .create();
        dlg.setOnShowListener(x -> dlg.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            String n = name.getText().toString().trim();
            if (n.isEmpty()) {
                toast(R.string.section_need_name);
                return;
            }
            dlg.dismiss();
            sectionCreate(n);
        }));
        showSafe(dlg);
    }

    private void sectionCreate(final String name) {
        final String board = prefs.boardId();
        final String base = prefs.base();
        final String key = prefs.key();
        pool.execute(() -> {
            int code = -1;
            try {
                JSONObject body = new JSONObject();
                body.put("name", name);
                code = Api.post(base + "/api/b/section/add?b=" + board, key, body.toString()).status;
            } catch (Api.AuthException e) {
                code = 401;
            } catch (Exception ignored) {
            }
            final int f = code;
            uiPost(() -> {
                if (f == 200) {
                    toast(R.string.section_created);
                    load();
                } else {
                    reportFailure(f);
                }
            });
        });
    }

    private void openSectionRename(final String id) {
        String current = "";
        for (String[] s : sections) {
            if (s[0].equals(id)) current = s[1];
        }
        final EditText name = makeInput(current, getString(R.string.section_name_hint), false);
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(20), dp(6), dp(20), 0);
        box.addView(name);
        final AlertDialog dlg = new AlertDialog.Builder(this)
                .setTitle(R.string.section_rename)
                .setView(box)
                .setPositiveButton(R.string.save, null)
                .setNegativeButton(R.string.key_cancel, null)
                .create();
        dlg.setOnShowListener(x -> dlg.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            String n = name.getText().toString().trim();
            if (n.isEmpty()) {
                toast(R.string.section_need_name);
                return;
            }
            dlg.dismiss();
            sectionRename(id, n);
        }));
        showSafe(dlg);
    }

    private void sectionRename(final String id, final String name) {
        final String board = prefs.boardId();
        final String base = prefs.base();
        final String key = prefs.key();
        pool.execute(() -> {
            int code = -1;
            try {
                // один атомарный rename вместо add+update+del: при обрыве
                // посередине задачи не остаются в удалённом разделе
                JSONObject body = new JSONObject();
                body.put("id", id);
                body.put("name", name);
                code = Api.post(base + "/api/b/section/set?b=" + board, key, body.toString()).status;
            } catch (Api.AuthException e) {
                code = 401;
            } catch (Exception ignored) {
            }
            final int f = code;
            uiPost(() -> {
                if (f == 200) {
                    toast(R.string.section_renamed);
                    load();
                } else {
                    reportFailure(f);
                }
            });
        });
    }

    private void confirmSectionDelete(final String id) {
        String label = id;
        for (String[] s : sections) {
            if (s[0].equals(id)) label = s[1];
        }
        showSafe(new AlertDialog.Builder(this)
                .setTitle(R.string.section_delete)
                .setMessage(getString(R.string.section_delete_confirm, label))
                .setPositiveButton(R.string.delete, (d, w) -> sectionDelete(id))
                .setNegativeButton(R.string.key_cancel, null)
                .create());
    }

    private void sectionDelete(final String id) {
        final String board = prefs.boardId();
        final String base = prefs.base();
        final String key = prefs.key();
        pool.execute(() -> {
            int code = -1;
            try {
                JSONObject body = new JSONObject();
                body.put("id", id);
                code = Api.post(base + "/api/b/section/del?b=" + board, key, body.toString()).status;
            } catch (Api.AuthException e) {
                code = 401;
            } catch (Exception ignored) {
            }
            final int f = code;
            uiPost(() -> {
                if (f == 200) {
                    toast(R.string.section_deleted);
                    if (id.equals(activeSection)) activeSection = "";
                    load();
                } else {
                    reportFailure(f);
                }
            });
        });
    }

    private void renderChips() {
        chips.removeAllViews();
        chipsWrap.setVisibility(View.VISIBLE);

        float d = getResources().getDisplayMetrics().density;
        chips.addView(chip(getString(R.string.all_sections), "", activeSection.isEmpty(), d));
        for (String[] s : sections) {
            chips.addView(chip(s[1], s[0], activeSection.equals(s[0]), d));
        }
    }

    private TextView chip(String label, final String id, boolean active, float d) {
        TextView tv = new TextView(this);
        tv.setText(label);
        tv.setTextSize(13f);
        tv.setTypeface(tv.getTypeface(), active ? Typeface.BOLD : Typeface.NORMAL);
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
        tv.setOnLongClickListener(v -> {
            openSectionMenu(id);
            return true;
        });
        return tv;
    }

    private void renderTasks(JSONObject o) {
        View empty = findViewById(R.id.empty);
        if (o == null) {
            if (empty != null) empty.setVisibility(View.GONE);
            return;
        }
        lastTasks = o;
        List<Object> rows = new ArrayList<>();
        JSONArray arr = o.optJSONArray("tasks");
        if (arr == null) {
            adapter.setItems(rows);
            if (empty != null) empty.setVisibility(tab == TAB_BOARD ? View.VISIBLE : View.GONE);
            return;
        }

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
                mt.status = st;
                mt.mode = t.optString("Mode", "task");
                mt.section = t.optString("Section", "");
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
        if (empty != null) {
            empty.setVisibility(rows.isEmpty() && tab == TAB_BOARD ? View.VISIBLE : View.GONE);
        }
    }

    private String unitLabel(String unit) {
        if (unit == null) return "";
        switch (unit) {
            case "шагов":
                return getString(R.string.unit_steps);
            case "эпох":
                return getString(R.string.unit_epochs);
            default:
                return unit;
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
            int from = Math.max(0, chain.length() - 8);
            for (int i = from; i < chain.length(); i++) {
                if (sb.length() > 0) sb.append('\n');
                sb.append(chain.optString(i));
            }
        }
        trChain.setText(sb.length() == 0 ? "—" : sb.toString());

        JSONArray log = o.optJSONArray("logLines");
        if (log == null) log = o.optJSONArray("lines");
        boolean hasLog = log != null && log.length() > 0;
        if (trLogCard != null) trLogCard.setVisibility(hasLog ? View.VISIBLE : View.GONE);
        if (hasLog) {
            StringBuilder tail = new StringBuilder();
            int from = Math.max(0, log.length() - 12);
            for (int i = from; i < log.length(); i++) {
                if (tail.length() > 0) tail.append('\n');
                tail.append(log.optString(i));
            }
            trTail.setText(tail.toString());
        }
    }

    // ---------- обновления ----------

    private void checkUpdates() {
        checkUpdates(false);
    }

    private void clearPendingUpdate() {
        pendingUpdate = "";
        pendingName = "";
        pendingNotes = "";
        pendingSha = "";
        pendingCode = 0;
        pendingApk = null;
        prefs.clearUpdate();
        // The downloaded APK is only a cache; never leave it behind forever.
        new File(getCacheDir(), "update.apk").delete();
    }

    private void checkUpdates(final boolean manual) {
        if (!pendingUpdate.isEmpty()) {
            if (pendingCode > BuildConfig.VERSION_CODE) {
                showUpdateStatus();
                return;
            }
            clearPendingUpdate();
            resetUpdateButton();
        }
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
                final String sha = o.optString("sha256", "").trim().toLowerCase(Locale.ROOT);
                if (code > BuildConfig.VERSION_CODE && !file.isEmpty()) {
                    uiPost(() -> {
                        pendingUpdate = file;
                        pendingName = name;
                        pendingNotes = notes;
                        pendingSha = sha;
                        pendingCode = code;
                        prefs.update(file, name, notes, sha, code);
                        showUpdateStatus();
                    });
                } else {
                    uiPost(() -> {
                        if (!pendingUpdate.isEmpty()) clearPendingUpdate();
                        resetUpdateButton();
                        if (manual) toast(R.string.update_none);
                    });
                }
            } catch (Exception ignored) {
            }
        });
    }

    private void showUpdateStatus() {
        if (pendingUpdate.isEmpty() || !alive()) return;
        String label = pendingName.isEmpty() ? pendingUpdate : pendingName;
        status.setText(getString(R.string.update_available, label));
        status.setTextColor(NxBackdrop.color(this, R.attr.nxPrimary));
        status.setTypeface(Typeface.DEFAULT_BOLD);
        status.setOnClickListener(v -> openUpdateDialog());
        View btn = findViewById(R.id.btn_update);
        if (btn != null) btn.setBackgroundResource(R.drawable.nx_pill_on);
    }

    private void resetUpdateButton() {
        View btn = findViewById(R.id.btn_update);
        if (btn != null) btn.setBackgroundResource(R.drawable.nx_card);
    }

    private void openUpdateDialog() {
        if (pendingUpdate.isEmpty() || !alive()) return;
        String label = pendingName.isEmpty() ? pendingUpdate : pendingName;
        String notes = pendingNotes.trim();
        showSafe(new AlertDialog.Builder(this)
                .setTitle(getString(R.string.update_dialog_title, label))
                .setMessage(notes.isEmpty() ? getString(R.string.update_no_notes) : notes)
                .setPositiveButton(R.string.update_install, (d, w) -> downloadUpdate())
                .setNegativeButton(R.string.close, null)
                .create());
    }

    private void downloadUpdate() {
        if (pendingUpdate.isEmpty()) {
            checkUpdates(true);
            return;
        }
        if (pendingApk != null && pendingApk.exists()) {
            install(pendingApk);
            return;
        }
        if (pendingSha.isEmpty()) {
            toast(R.string.update_no_sha);
            return;
        }
        final String file = pendingUpdate;
        final String expect = pendingSha;
        final String key = prefs.key();
        final String base = prefs.base();
        status.setText(R.string.update_dl);
        status.setOnClickListener(v -> openUpdateDialog());
        pool.execute(() -> {
            File out = new File(getCacheDir(), "update.apk");
            String got = "";
            int code = 0;
            try {
                got = Api.download(base + "/app/" + file, key, out);
            } catch (Api.AuthException e) {
                code = 401;
            } catch (Exception ignored) {
                code = -1;
            }
            final int f = code;
            final String g = got;
            final File o = out;
            uiPost(() -> {
                if (f != 0) {
                    reportFailure(f);
                    showUpdateStatus();
                    return;
                }
                if (!g.equalsIgnoreCase(expect)) {
                    o.delete();
                    toast(R.string.update_sha_bad);
                    showUpdateStatus();
                    return;
                }
                pendingApk = o;
                install(o);
            });
        });
    }

    private void install(File apk) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                && !getPackageManager().canRequestPackageInstalls()) {
            showSafe(new AlertDialog.Builder(this)
                    .setTitle(R.string.install_denied_title)
                    .setMessage(R.string.install_denied_body)
                    .setPositiveButton(R.string.install_settings, (d, w) -> {
                        try {
                            startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                                    Uri.parse("package:" + getPackageName())));
                        } catch (Exception e) {
                            toast(R.string.net_error);
                        }
                    })
                    .setNegativeButton(R.string.close, null)
                    .create());
            return;
        }
        status.setText(R.string.update_ready);
        Uri uri = FileProvider.getUriForFile(this, getPackageName() + ".fileprovider", apk);
        Intent i = new Intent(Intent.ACTION_VIEW);
        i.setDataAndType(uri, "application/vnd.android.package-archive");
        i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        try {
            startActivity(i);
            // Do not clear yet: startActivity only opens the system installer.
            // The user may cancel it, and then the update must stay available.
            // onResume clears the offer once the new versionCode is installed.
        } catch (Exception e) {
            toast(R.string.install_failed);
        }
    }

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            if (!alive()) return;
            load();
            ui.postDelayed(this, lastFailed ? REFRESH_SLOW_MS : REFRESH_MS);
        }
    };

    @Override
    protected void onResume() {
        super.onResume();
        if (!pendingUpdate.isEmpty() && pendingCode > 0 && BuildConfig.VERSION_CODE >= pendingCode) {
            clearPendingUpdate();
            resetUpdateButton();
        }
        if (!loading) load();
        ui.removeCallbacks(tick);
        ui.postDelayed(tick, REFRESH_MS);
        if (tab == TAB_VIEWS) {
            loadViews(true);
            ui.removeCallbacks(viewsTick);
            ui.postDelayed(viewsTick, VIEWS_MS);
        }
    }

    @Override
    protected void onPause() {
        ui.removeCallbacks(tick);
        ui.removeCallbacks(viewsTick);
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        ui.removeCallbacksAndMessages(null);
        if (openDialog != null) {
            try {
                openDialog.dismiss();
            } catch (Exception ignored) {
            }
            openDialog = null;
        }
        pool.shutdownNow();
        super.onDestroy();
    }
}
