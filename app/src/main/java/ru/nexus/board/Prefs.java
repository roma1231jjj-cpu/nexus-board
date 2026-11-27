package ru.nexus.board;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONObject;

public final class Prefs {

    public static final String DEFAULT_BASE = "https://board.nexus-your-heper.ru";

    /** Один запрос на создание доски на весь процесс: экраны не плодят доски. */
    private static final Object BOARD_LOCK = new Object();

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
        String v = sp.getString("boardId", "");
        return v == null ? "" : v.trim();
    }

    public void boardId(String v) {
        sp.edit().putString("boardId", v == null ? "" : v.trim()).apply();
    }

    public void clearBoard() {
        sp.edit().remove("boardId").apply();
    }

    public boolean nexusMode() {
        return !key().isEmpty();
    }

    /** Доска открыта личным ключом, а не ключом владельца: тренировка скрыта. */
    public boolean personal() {
        return sp.getBoolean("personal", false);
    }

    public void personal(boolean v) {
        sp.edit().putBoolean("personal", v).apply();
    }

    public boolean trainTab() {
        return sp.getBoolean("trainTab", false);
    }

    public void trainTab(boolean v) {
        sp.edit().putBoolean("trainTab", v).apply();
    }

    public int tabMode() {
        return sp.getInt("tabMode", sp.getBoolean("trainTab", false) ? 1 : 0);
    }

    public void tabMode(int v) {
        sp.edit().putInt("tabMode", v).putBoolean("trainTab", v == 1).apply();
    }

    public long lastUpdateCheck() {
        return sp.getLong("updCheck", 0L);
    }

    public void lastUpdateCheck(long v) {
        sp.edit().putLong("updCheck", v).apply();
    }

    // ---------- отложенное обновление ----------

    public String updFile() {
        return sp.getString("updFile", "");
    }

    public String updName() {
        return sp.getString("updName", "");
    }

    public String updNotes() {
        return sp.getString("updNotes", "");
    }

    public String updSha() {
        return sp.getString("updSha", "");
    }

    public int updCode() {
        return sp.getInt("updCode", 0);
    }

    public void update(String file, String name, String notes, String sha, int code) {
        sp.edit()
                .putString("updFile", file == null ? "" : file)
                .putString("updName", name == null ? "" : name)
                .putString("updNotes", notes == null ? "" : notes)
                .putString("updSha", sha == null ? "" : sha)
                .putInt("updCode", code)
                .apply();
    }

    public void clearUpdate() {
        sp.edit().remove("updFile").remove("updName").remove("updNotes").remove("updSha")
                .remove("updCode").apply();
    }

    /**
     * Возвращает ключ доски, создавая её при необходимости. Блокирующий вызов:
     * дергать только из фонового потока. Повторные вызовы из разных экранов
     * ждут один и тот же запрос и получают тот же ключ.
     */
    public static String ensureBoard(Context c) {
        Prefs p = new Prefs(c);
        String id = p.boardId();
        if (validBoardId(id)) return id;
        synchronized (BOARD_LOCK) {
            id = p.boardId();
            if (validBoardId(id)) return id;
            try {
                // The site token gates /api/b/new; without it an owner with
                // no board yet could never create the first one.
                JSONObject o = Api.postJson(p.base() + "/api/b/new", p.key(), "{}");
                String fresh = o.optString("id", "").trim().toLowerCase(java.util.Locale.ROOT);
                if (validBoardId(fresh)) {
                    p.boardId(fresh);
                    return fresh;
                }
            } catch (Exception ignored) {
            }
            return "";
        }
    }

    public static boolean validBoardId(String id) {
        return id != null && id.matches("^[a-f0-9]{16}$");
    }
}
