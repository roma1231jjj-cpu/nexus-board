package ru.nexus.board;

import android.content.Context;
import android.content.res.Configuration;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.widget.EditText;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;

import org.json.JSONObject;

import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class CodeActivity extends AppCompatActivity {

    public static final String EXTRA_ID = "id";
    public static final String EXTRA_BOARD = "board";
    public static final String EXTRA_TITLE = "title";

    private Prefs prefs;
    private final ExecutorService pool = Executors.newSingleThreadExecutor();
    private final Handler ui = new Handler(Looper.getMainLooper());

    private EditText editor;
    private TextView title;
    private TextView status;

    private String id = "";
    private String board = "";
    private int version = 0;
    private String loaded = "";
    private volatile boolean busy = false;

    @Override
    protected void onCreate(Bundle b) {
        prefs = new Prefs(this);
        applyTheme();
        super.onCreate(b);
        setContentView(R.layout.activity_code);
        NxBackdrop.apply(findViewById(R.id.root));

        id = safe(getIntent().getStringExtra(EXTRA_ID));
        board = safe(getIntent().getStringExtra(EXTRA_BOARD));
        if (board.isEmpty()) board = prefs.boardId();

        editor = findViewById(R.id.code_editor);
        title = findViewById(R.id.code_title);
        status = findViewById(R.id.code_status);

        String extra = safe(getIntent().getStringExtra(EXTRA_TITLE));
        if (!extra.isEmpty()) title.setText(extra);

        findViewById(R.id.code_back).setOnClickListener(v -> confirmExit());
        findViewById(R.id.code_pull).setOnClickListener(v -> pull(true));
        findViewById(R.id.code_save).setOnClickListener(v -> save());

        setStatus(getString(R.string.loading), false);
        pull(false);
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

    private static String safe(String v) {
        return v == null ? "" : v;
    }

    private boolean alive() {
        return !isFinishing() && !isDestroyed();
    }

    private void uiPost(Runnable r) {
        ui.post(() -> {
            if (alive()) r.run();
        });
    }

    private int warningColor() {
        return "frost".equals(prefs.theme()) ? 0xFFB45309 : 0xFFF0B429;
    }

    private void setStatus(String text, boolean warn) {
        status.setVisibility(View.VISIBLE);
        status.setText(text);
        status.setTextColor(warn ? warningColor() : NxBackdrop.color(this, R.attr.nxTextDim));
    }

    private boolean isDirty() {
        return editor != null && !editor.getText().toString().equals(loaded);
    }

    private void confirmExit() {
        if (!isDirty()) {
            finish();
            return;
        }
        new AlertDialog.Builder(this)
                .setTitle(R.string.code_exit_title)
                .setMessage(R.string.code_exit_body)
                .setPositiveButton(R.string.code_exit_discard, (d, w) -> finish())
                .setNegativeButton(R.string.key_cancel, null)
                .show();
    }

    @Override
    public void onBackPressed() {
        confirmExit();
    }

    private void pull(boolean confirm) {
        if (busy) return;
        if (confirm && isDirty()) {
            new AlertDialog.Builder(this)
                    .setTitle(R.string.code_pull)
                    .setMessage(R.string.code_pull_confirm)
                    .setPositiveButton(R.string.code_pull, (d, w) -> doPull())
                    .setNegativeButton(R.string.key_cancel, null)
                    .show();
            return;
        }
        doPull();
    }

    private void doPull() {
        if (id.isEmpty()) {
            setStatus(getString(R.string.code_not_found), true);
            return;
        }
        busy = true;
        setStatus(getString(R.string.loading), false);
        final String base = prefs.base();
        final String key = prefs.key();
        final String bid = board;
        final String cid = id;
        pool.execute(() -> {
            JSONObject o = null;
            try {
                o = Api.code(base, bid, key, cid);
            } catch (Exception ignored) {
            }
            final JSONObject fo = o;
            uiPost(() -> {
                busy = false;
                if (fo == null) {
                    setStatus(getString(R.string.net_error), true);
                    return;
                }
                apply(fo.optJSONObject("code"));
            });
        });
    }

    private void apply(JSONObject code) {
        if (code == null) {
            setStatus(getString(R.string.code_not_found), true);
            return;
        }
        String content = code.optString("content", "");
        version = code.optInt("version", 1);
        loaded = content;
        String t = code.optString("title", "");
        if (!t.isEmpty()) title.setText(t);
        editor.setText(content);
        editor.setSelection(0);
        setStatus(meta(code), false);
    }

    private String meta(JSONObject code) {
        String file = code.optString("filename", "");
        if (file.isEmpty()) {
            file = code.optString("lang", "");
        }
        StringBuilder sb = new StringBuilder("v").append(version);
        if (!file.isEmpty()) sb.append(" · ").append(file);
        String sha = code.optString("sha", "");
        if (!sha.isEmpty()) sb.append(" · ").append(sha);
        return sb.toString();
    }

    private void save() {
        if (busy) return;
        final String content = editor.getText().toString();
        if (content.trim().isEmpty()) {
            Toast.makeText(this, R.string.code_empty, Toast.LENGTH_SHORT).show();
            return;
        }
        busy = true;
        setStatus(getString(R.string.code_saving), false);
        final String base = prefs.base();
        final String key = prefs.key();
        final String bid = board;
        final String cid = id;
        final int ifVersion = version;
        pool.execute(() -> {
            Api.Reply r = null;
            try {
                r = Api.codeSave(base, bid, key, cid, content, ifVersion);
            } catch (Exception ignored) {
            }
            final Api.Reply fr = r;
            uiPost(() -> {
                busy = false;
                if (fr == null) {
                    setStatus(getString(R.string.net_error), true);
                    return;
                }
                if (fr.status == 200) {
                    JSONObject o = fr.json();
                    version = o.optInt("version", version + 1);
                    loaded = content;
                    setStatus(getString(R.string.code_saved, version), false);
                    Toast.makeText(this, getString(R.string.code_saved, version), Toast.LENGTH_SHORT).show();
                    return;
                }
                if (fr.status == 409) {
                    JSONObject o = fr.json();
                    conflict(o.optInt("version", version + 1), o.optString("content", ""));
                    return;
                }
                if (fr.status == 404) {
                    setStatus(getString(R.string.code_not_found), true);
                    return;
                }
                setStatus(getString(R.string.code_http, String.valueOf(fr.status)), true);
            });
        });
    }

    private void conflict(final int serverVersion, final String serverContent) {
        setStatus(getString(R.string.code_conflict_title), true);
        new AlertDialog.Builder(this)
                .setTitle(R.string.code_conflict_title)
                .setMessage(getString(R.string.code_conflict_body, serverVersion))
                .setPositiveButton(R.string.code_conflict_take, (d, w) -> {
                    loaded = serverContent;
                    version = serverVersion;
                    editor.setText(serverContent);
                    editor.setSelection(0);
                    setStatus("v" + version + " · " + getString(R.string.code_took), false);
                })
                .setNeutralButton(R.string.code_conflict_keep, (d, w) -> {
                    version = serverVersion;
                    setStatus(getString(R.string.code_keep, serverVersion), true);
                })
                .setNegativeButton(R.string.key_cancel, null)
                .show();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        pool.shutdownNow();
    }
}
