package ru.nexus.board;

import android.content.Context;
import android.content.Intent;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

final class BlocksView {

    private static final int OK = 0xFF34D399;
    private static final int WARN = 0xFFF0B429;
    private static final int ERR = 0xFFF87171;
    private static final int IDLE = 0xFF9B93B4;

    private final Context c;
    private final float d;
    private final JSONObject tasks;
    private final String board;

    BlocksView(Context c, JSONObject tasks, String board) {
        this.c = c;
        this.d = c.getResources().getDisplayMetrics().density;
        this.tasks = tasks;
        this.board = board == null ? "" : board;
    }

    View build(JSONObject b) {
        if (b == null) return null;
        JSONObject body = b.optJSONObject("body");
        if (body == null) body = new JSONObject();
        String kind = b.optString("kind", "");
        switch (kind) {
            case "metric":
                return metric(b, body);
            case "progress":
                return progress(b, body);
            case "status":
                return status(b, body);
            case "text":
                return text(b, body);
            case "log":
                return log(b, body);
            case "links":
                return links(b, body);
            case "chart":
                return chart(b, body);
            case "code":
                return code(b, body);
            case "tasks":
                return tasksCard(b);
            case "activity":
                return activity(b);
            default:
                return null;
        }
    }

    private int px(float v) {
        return Math.round(v * d);
    }

    private int textColor() {
        return NxBackdrop.color(c, R.attr.nxText);
    }

    private int dimColor() {
        return NxBackdrop.color(c, R.attr.nxTextDim);
    }

    private int primaryColor() {
        return NxBackdrop.color(c, R.attr.nxPrimary);
    }

    private int onPrimaryColor() {
        return NxBackdrop.color(c, R.attr.nxOnPrimary);
    }

    private LinearLayout card() {
        LinearLayout box = new LinearLayout(c);
        box.setOrientation(LinearLayout.VERTICAL);
        int p = px(16);
        box.setPadding(p, p, p, p);
        box.setBackgroundResource(R.drawable.nx_card);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.bottomMargin = px(10);
        box.setLayoutParams(lp);
        return box;
    }

    private LinearLayout.LayoutParams fill() {
        return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private TextView label(String s, float size, int color) {
        TextView tv = new TextView(c);
        tv.setText(s == null ? "" : s);
        tv.setTextSize(size);
        tv.setLetterSpacing(0.14f);
        tv.setTextColor(color);
        return tv;
    }

    private void head(LinearLayout box, JSONObject b, int fallback) {
        String title = b.optString("title", "").trim();
        if (title.isEmpty() && fallback != 0) title = c.getString(fallback);
        if (title.isEmpty()) return;
        TextView tv = label(title.toUpperCase(Locale.getDefault()), 10.5f, dimColor());
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.bottomMargin = px(8);
        box.addView(tv, lp);
    }

    private TextView value(String s, float size) {
        TextView tv = new TextView(c);
        tv.setText(s == null ? "" : s);
        tv.setTextSize(size);
        tv.setTextColor(textColor());
        tv.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        return tv;
    }

    private TextView note(String s, float size) {
        TextView tv = new TextView(c);
        tv.setText(s == null ? "" : s);
        tv.setTextSize(size);
        tv.setTextColor(dimColor());
        tv.setLineSpacing(px(3), 1f);
        return tv;
    }

    private View metric(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        head(box, b, 0);

        LinearLayout row = new LinearLayout(c);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.BOTTOM);

        TextView main = value(body.optString("value", "—"), 28f);
        main.setLetterSpacing(0f);
        row.addView(main);

        String unit = body.optString("unit", "");
        if (!unit.isEmpty()) {
            TextView u = note(unit, 13f);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.setMarginStart(px(6));
            lp.bottomMargin = px(4);
            row.addView(u, lp);
        }
        box.addView(row, fill());

        String sub = body.optString("sub", "");
        if (!sub.isEmpty()) {
            TextView s = note(sub, 12.5f);
            LinearLayout.LayoutParams lp = fill();
            lp.topMargin = px(4);
            box.addView(s, lp);
        }

        List<Float> vals = new ArrayList<>();
        JSONArray hist = body.optJSONArray("history");
        if (hist != null) {
            for (int i = 0; i < hist.length(); i++) {
                JSONObject h = hist.optJSONObject(i);
                double v = h == null ? Double.NaN : h.optDouble("v", Double.NaN);
                if (!Double.isNaN(v)) vals.add((float) v);
            }
        }
        if (vals.size() >= 2) {
            SparkView sp = new SparkView(c);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, px(56));
            lp.topMargin = px(10);
            box.addView(sp, lp);
            sp.setData(vals);
        }
        return box;
    }

    private View progress(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        head(box, b, 0);

        int pct = Math.max(0, Math.min(100, body.optInt("value", 0)));

        LinearLayout row = new LinearLayout(c);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        String name = body.optString("label", "");
        if (name.isEmpty()) name = b.optString("title", "");
        TextView left = new TextView(c);
        left.setText(name.isEmpty() ? String.valueOf(pct) + "%" : name);
        left.setTextSize(13.5f);
        left.setTextColor(textColor());
        left.setMaxLines(2);
        LinearLayout.LayoutParams llp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        row.addView(left, llp);

        TextView right = value(pct + "%", 15f);
        right.setLetterSpacing(0f);
        row.addView(right);
        box.addView(row, fill());

        ProgressBar bar = new ProgressBar(c, null, android.R.attr.progressBarStyleHorizontal);
        bar.setMax(100);
        bar.setProgress(pct);
        bar.setProgressDrawable(c.getDrawable(R.drawable.nx_bar));
        LinearLayout.LayoutParams blp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, px(8));
        blp.topMargin = px(12);
        box.addView(bar, blp);

        String eta = body.optString("eta", "");
        if (!eta.isEmpty()) {
            TextView e = note(c.getString(R.string.views_eta, eta), 12.5f);
            LinearLayout.LayoutParams lp = fill();
            lp.topMargin = px(10);
            box.addView(e, lp);
        }
        return box;
    }

    private View dot(int color) {
        View v = new View(c);
        int s = px(8);
        GradientDrawable g = new GradientDrawable();
        g.setShape(GradientDrawable.OVAL);
        g.setColor(color);
        v.setBackground(g);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(s, s);
        lp.setMarginEnd(px(10));
        lp.topMargin = px(5);
        v.setLayoutParams(lp);
        return v;
    }

    private int stateColor(String st) {
        if ("ok".equals(st)) return OK;
        if ("warn".equals(st)) return WARN;
        if ("err".equals(st)) return ERR;
        return IDLE;
    }

    private View status(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        head(box, b, 0);
        JSONArray items = body.optJSONArray("items");
        if (items == null || items.length() == 0) {
            box.addView(note(c.getString(R.string.views_no_data), 12.5f), fill());
            return box;
        }
        for (int i = 0; i < items.length(); i++) {
            JSONObject it = items.optJSONObject(i);
            if (it == null) continue;
            LinearLayout row = new LinearLayout(c);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.TOP);
            row.addView(dot(stateColor(it.optString("state", "idle"))));

            LinearLayout col = new LinearLayout(c);
            col.setOrientation(LinearLayout.VERTICAL);

            TextView name = new TextView(c);
            name.setText(it.optString("label", ""));
            name.setTextSize(13.5f);
            name.setTextColor(textColor());
            col.addView(name);

            String n = it.optString("note", "");
            if (!n.isEmpty()) {
                TextView nt = note(n, 12f);
                LinearLayout.LayoutParams lp = fill();
                lp.topMargin = px(2);
                col.addView(nt, lp);
            }
            row.addView(col, new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

            LinearLayout.LayoutParams lp = fill();
            if (i > 0) lp.topMargin = px(10);
            box.addView(row, lp);
        }
        return box;
    }

    private View text(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        head(box, b, 0);
        TextView tv = note(body.optString("text", ""), 13.5f);
        tv.setTextColor(textColor());
        box.addView(tv, fill());
        return box;
    }

    private View log(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        String title = b.optString("title", "").trim();
        if (title.isEmpty()) {
            String path = (b.optJSONObject("source") != null
                    ? b.optJSONObject("source").optString("path", "")
                    : "");
            if (path.isEmpty()) path = body.optString("path", "");
            int cut = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
            title = cut >= 0 ? path.substring(cut + 1) : path;
        }
        head(box, b, 0);

        List<String> lines = new ArrayList<>();
        JSONObject tail = b.optJSONObject("tail");
        String tailError = tail == null ? "" : tail.optString("error", "");
        JSONArray arr = tail == null ? b.optJSONArray("tail") : tail.optJSONArray("lines");
        if (arr != null) {
            for (int i = 0; i < arr.length(); i++) lines.add(arr.optString(i, ""));
        }
        while (lines.size() > 26) lines.remove(0);

        TextView tv = new TextView(c);
        tv.setTextSize(11f);
        tv.setTextColor(dimColor());
        tv.setTypeface(android.graphics.Typeface.MONOSPACE);
        tv.setLineSpacing(px(2), 1f);
        tv.setHorizontallyScrolling(true);
        String bodyText;
        if (!lines.isEmpty()) bodyText = TextUtils.join("\n", lines);
        else bodyText = tailError.isEmpty() ? c.getString(R.string.views_log_empty) : tailError;
        tv.setText(bodyText);

        HorizontalScrollView hsv = new HorizontalScrollView(c);
        hsv.setHorizontalScrollBarEnabled(false);
        hsv.addView(tv, new HorizontalScrollView.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        box.addView(hsv, fill());
        return box;
    }

    private View links(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        head(box, b, 0);
        JSONArray items = body.optJSONArray("items");
        if (items == null || items.length() == 0) {
            box.addView(note(c.getString(R.string.views_no_data), 12.5f), fill());
            return box;
        }
        for (int i = 0; i < items.length(); i++) {
            final JSONObject it = items.optJSONObject(i);
            if (it == null) continue;
            final String url = it.optString("url", "");

            LinearLayout row = new LinearLayout(c);
            row.setOrientation(LinearLayout.VERTICAL);
            int p = px(12);
            row.setPadding(p, p, p, p);
            row.setBackgroundResource(R.drawable.nx_chip);

            TextView name = new TextView(c);
            name.setText(it.optString("label", url));
            name.setTextSize(13.5f);
            name.setTextColor(primaryColor());
            name.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
            row.addView(name);

            String n = it.optString("note", "");
            if (!n.isEmpty()) {
                TextView nt = note(n, 12f);
                LinearLayout.LayoutParams lp = fill();
                lp.topMargin = px(2);
                row.addView(nt, lp);
            }

            if (url.startsWith("http://") || url.startsWith("https://")) {
                row.setOnClickListener(v -> openUrl(url));
            }

            LinearLayout.LayoutParams lp = fill();
            if (i > 0) lp.topMargin = px(8);
            box.addView(row, lp);
        }
        return box;
    }

    private void openUrl(String url) {
        try {
            Intent i = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            c.startActivity(i);
        } catch (Exception e) {
            Toast.makeText(c, R.string.net_error, Toast.LENGTH_SHORT).show();
        }
    }

    private View chart(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        head(box, b, R.string.views_chart);
        List<Float> vals = new ArrayList<>();
        JSONArray series = body.optJSONArray("series");
        if (series != null && series.length() > 0) {
            JSONObject s0 = series.optJSONObject(0);
            JSONArray pts = s0 == null ? null : s0.optJSONArray("points");
            if (pts != null) {
                for (int i = 0; i < pts.length(); i++) {
                    double v = pts.optDouble(i, Double.NaN);
                    if (!Double.isNaN(v)) vals.add((float) v);
                }
            }
        }
        if (vals.size() < 2) {
            box.addView(note(c.getString(R.string.views_no_data), 12.5f), fill());
            return box;
        }
        SparkView sp = new SparkView(c);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, px(86));
        lp.topMargin = px(8);
        box.addView(sp, lp);
        sp.setData(vals);
        return box;
    }

    private View code(JSONObject b, JSONObject body) {
        LinearLayout box = card();
        head(box, b, R.string.code_title);

        LinearLayout row = new LinearLayout(c);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        String file = body.optString("filename", "");
        if (file.isEmpty()) file = body.optString("lang", "");
        int version = body.optInt("version", 1);
        String meta = file.isEmpty() ? "v" + version : file + " · v" + version;

        TextView left = note(meta, 12.5f);
        left.setSingleLine(true);
        left.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);
        row.addView(left, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView open = new TextView(c);
        open.setText(c.getString(R.string.views_open));
        open.setTextSize(12.5f);
        open.setTypeface(android.graphics.Typeface.DEFAULT_BOLD);
        open.setTextColor(onPrimaryColor());
        int ph = px(16);
        int pv = px(8);
        open.setPadding(ph, pv, ph, pv);
        open.setBackgroundResource(R.drawable.nx_pill_on);
        row.addView(open);

        final String id = b.optString("id", "");
        open.setOnClickListener(v -> {
            if (id.isEmpty()) return;
            Intent i = new Intent(c, CodeActivity.class);
            i.putExtra(CodeActivity.EXTRA_ID, id);
            i.putExtra(CodeActivity.EXTRA_BOARD, board);
            i.putExtra(CodeActivity.EXTRA_TITLE, b.optString("title", ""));
            c.startActivity(i);
        });
        box.addView(row, fill());
        return box;
    }

    private View tasksCard(JSONObject b) {
        LinearLayout box = card();
        head(box, b, R.string.views_tasks);

        int doing = 0, done = 0, total = 0;
        JSONArray arr = tasks == null ? null : tasks.optJSONArray("tasks");
        if (arr != null) {
            for (int i = 0; i < arr.length(); i++) {
                JSONObject t = arr.optJSONObject(i);
                if (t == null) continue;
                total++;
                String st = t.optString("Status", "todo");
                if ("doing".equals(st)) doing++;
                else if ("done".equals(st)) done++;
            }
        }

        LinearLayout row = new LinearLayout(c);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.addView(counter(String.valueOf(doing), c.getString(R.string.views_tasks_doing)), weight());
        row.addView(counter(String.valueOf(done), c.getString(R.string.views_tasks_done)), weight());
        row.addView(counter(String.valueOf(total), c.getString(R.string.views_tasks_total)), weight());
        LinearLayout.LayoutParams lp = fill();
        lp.topMargin = px(4);
        box.addView(row, lp);
        return box;
    }

    private LinearLayout.LayoutParams weight() {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        lp.setMarginEnd(px(8));
        return lp;
    }

    private View counter(String num, String name) {
        LinearLayout col = new LinearLayout(c);
        col.setOrientation(LinearLayout.VERTICAL);
        TextView v = value(num, 22f);
        v.setLetterSpacing(0f);
        col.addView(v);
        TextView l = label(name.toUpperCase(Locale.getDefault()), 10f, dimColor());
        LinearLayout.LayoutParams lp = fill();
        lp.topMargin = px(2);
        col.addView(l, lp);
        return col;
    }

    private View activity(JSONObject b) {
        LinearLayout box = card();
        head(box, b, R.string.views_activity);
        box.addView(note(c.getString(R.string.views_activity_hint), 12.5f), fill());
        return box;
    }
}
