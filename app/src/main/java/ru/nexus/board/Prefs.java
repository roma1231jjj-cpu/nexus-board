package ru.nexus.board;

import android.content.Context;
import android.content.SharedPreferences;

public final class Prefs {

    public static final String DEFAULT_BASE = "https://board.nexus-your-heper.ru";
    private final SharedPreferences sp;

    public Prefs(Context c) {
        sp = c.getApplicationContext().getSharedPreferences("board", Context.MODE_PRIVATE);
    }

    public String key() {
        return sp.getString("key", "");
    }

    public void key(String v) {
        sp.edit().putString("key", v == null ? "" : v.trim()).apply();
    }

    public String base() {
        String v = sp.getString("base", DEFAULT_BASE);
        if (v == null || v.isEmpty()) v = DEFAULT_BASE;
        while (v.endsWith("/")) v = v.substring(0, v.length() - 1);
        return v;
    }

    public void base(String v) {
        sp.edit().putString("base", v == null ? "" : v.trim()).apply();
    }

    public String theme() {
        return sp.getString("theme", "midnight");
    }

    public void theme(String v) {
        sp.edit().putString("theme", v).apply();
    }

    public String lang() {
        String v = sp.getString("lang", "");
        if (v == null || v.isEmpty()) {
            v = java.util.Locale.getDefault().getLanguage().startsWith("ru") ? "ru" : "en";
        }
        return v;
    }

    public void lang(String v) {
        sp.edit().putString("lang", v).apply();
    }


    public String boardId() {
        return sp.getString("boardId", "");
    }

    public void boardId(String v) {
        sp.edit().putString("boardId", v == null ? "" : v.trim()).apply();
    }

    public boolean nexusMode() {
        return !key().isEmpty();
    }

    public boolean trainTab() {
        return sp.getBoolean("trainTab", false);
    }

    public void trainTab(boolean v) {
        sp.edit().putBoolean("trainTab", v).apply();
    }

    public long lastUpdateCheck() {
        return sp.getLong("updCheck", 0L);
    }

    public void lastUpdateCheck(long v) {
        sp.edit().putLong("updCheck", v).apply();
    }
}
