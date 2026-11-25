package ru.nexus.board;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;

public final class Api {

    public static class AuthException extends IOException {
        public AuthException(String m) {
            super(m);
        }
    }

    private static String fetch(String url, String key) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(6000);
        c.setReadTimeout(9000);
        c.setRequestProperty("Accept", "application/json");
        c.setRequestProperty("User-Agent", "NexusBoard/" + BuildConfig.VERSION_NAME);
        if (key != null && !key.isEmpty()) {
            c.setRequestProperty("Cookie", "nxt=" + key);
        }
        int code = c.getResponseCode();
        if (code == 401 || code == 403) {
            c.disconnect();
            throw new AuthException("auth");
        }
        if (code != 200) {
            c.disconnect();
            throw new IOException("HTTP " + code);
        }
        InputStream in = c.getInputStream();
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int r;
        while ((r = in.read(buf)) > 0) out.write(buf, 0, r);
        in.close();
        c.disconnect();
        return out.toString("UTF-8");
    }

    public static final class Reply {
        public final int status;
        public final String body;

        Reply(int status, String body) {
            this.status = status;
            this.body = body == null ? "" : body;
        }

        public JSONObject json() {
            try {
                return new JSONObject(body);
            } catch (Exception e) {
                return new JSONObject();
            }
        }
    }

    private static String withKey(String url, String key) {
        if (key == null || key.isEmpty()) return url;
        return url + (url.contains("?") ? "&" : "?") + "k=" + URLEncoder.encode(key);
    }

    public static JSONObject json(String base, String path, String key) throws IOException {
        String raw = fetch(withKey(base + path, key), key);
        try {
            return new JSONObject(raw);
        } catch (Exception e) {
            throw new IOException("bad json");
        }
    }


    public static JSONObject postJson(String url, String key, String jsonBody) throws IOException {
        Reply r = post(url, key, jsonBody);
        if (r.status == 401 || r.status == 403) throw new AuthException("auth");
        if (r.status != 200) throw new IOException("HTTP " + r.status);
        try {
            return new JSONObject(r.body);
        } catch (Exception e) {
            throw new IOException("bad json");
        }
    }

    public static Reply post(String url, String key, String jsonBody) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(withKey(url, key)).openConnection();
        c.setConnectTimeout(6000);
        c.setReadTimeout(9000);
        c.setRequestMethod("POST");
        c.setDoOutput(true);
        c.setRequestProperty("Content-Type", "application/json; charset=utf-8");
        c.setRequestProperty("User-Agent", "NexusBoard/" + BuildConfig.VERSION_NAME);
        if (key != null && !key.isEmpty()) c.setRequestProperty("Cookie", "nxt=" + key);
        byte[] body = (jsonBody == null ? "{}" : jsonBody).getBytes("UTF-8");
        c.setFixedLengthStreamingMode(body.length);
        java.io.OutputStream os = c.getOutputStream();
        os.write(body);
        os.close();
        int code = c.getResponseCode();
        java.io.InputStream in = code >= 400 ? c.getErrorStream() : c.getInputStream();
        java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
        if (in != null) {
            byte[] buf = new byte[8192];
            int r;
            while ((r = in.read(buf)) > 0) out.write(buf, 0, r);
            in.close();
        }
        c.disconnect();
        if (code == 401 || code == 403) throw new AuthException("auth");
        return new Reply(code, out.toString("UTF-8"));
    }

    public static JSONObject views(String base, String board, String key) throws IOException {
        return json(base, "/api/b/views?b=" + URLEncoder.encode(board), key);
    }

    public static JSONObject code(String base, String board, String key, String id) throws IOException {
        return json(base, "/api/b/code?b=" + URLEncoder.encode(board)
                + "&id=" + URLEncoder.encode(id), key);
    }

    public static Reply codeSave(String base, String board, String key, String id,
                                 String content, int ifVersion) throws IOException {
        JSONObject o = new JSONObject();
        try {
            o.put("id", id);
            o.put("content", content == null ? "" : content);
            o.put("ifVersion", ifVersion);
        } catch (Exception ignored) {
        }
        return post(base + "/api/b/code/save?b=" + URLEncoder.encode(board), key, o.toString());
    }

    public static byte[] bytes(String url, String key) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(withKey(url, key)).openConnection();
        c.setConnectTimeout(10000);
        c.setReadTimeout(30000);
        if (key != null && !key.isEmpty()) c.setRequestProperty("Cookie", "nxt=" + key);
        int code = c.getResponseCode();
        if (code == 401 || code == 403) {
            c.disconnect();
            throw new AuthException("auth");
        }
        if (code != 200) {
            c.disconnect();
            throw new IOException("HTTP " + code);
        }
        InputStream in = c.getInputStream();
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[16384];
        int r;
        while ((r = in.read(buf)) > 0) out.write(buf, 0, r);
        in.close();
        c.disconnect();
        return out.toByteArray();
    }
}
