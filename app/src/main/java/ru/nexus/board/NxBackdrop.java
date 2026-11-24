package ru.nexus.board;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;
import android.util.TypedValue;
import android.view.View;

/** Фоновые блики темы: тот же вид, что на сайте и в других приложениях Nexus. */
public final class NxBackdrop extends Drawable {

    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final int base;
    private final int[] colors;
    private final float density;
    private int w, h;

    private NxBackdrop(int base, int[] colors, float density) {
        this.base = base;
        this.colors = colors;
        this.density = density <= 0f ? 1f : density;
    }

    public static int color(Context c, int attr) {
        TypedValue tv = new TypedValue();
        if (c.getTheme().resolveAttribute(attr, tv, true)) {
            if (tv.resourceId != 0) return c.getColor(tv.resourceId);
            return tv.data;
        }
        return Color.WHITE;
    }

    public static void apply(View root) {
        if (root == null || root.getContext() == null) return;
        Context c = root.getContext();
        NxBackdrop d = new NxBackdrop(
                color(c, R.attr.nxBg),
                new int[]{
                        color(c, R.attr.nxBlob1),
                        color(c, R.attr.nxBlob2),
                        color(c, R.attr.nxBlob3)},
                c.getResources().getDisplayMetrics().density);
        root.setBackground(d);
    }

    @Override
    protected void onBoundsChange(android.graphics.Rect b) {
        w = b.width();
        h = b.height();
    }

    @Override
    public void draw(Canvas canvas) {
        if (w <= 0 || h <= 0) return;
        canvas.drawColor(base);
        glow(canvas, w * 0.26f, h * 0.20f, density * 380f, colors[0]);
        glow(canvas, w * 0.94f, h * 0.72f, density * 340f, colors[1]);
        glow(canvas, w * 0.50f, h * 1.02f, density * 280f, colors[2]);
    }

    private void glow(Canvas canvas, float cx, float cy, float radius, int color) {
        int mid = (color & 0x00FFFFFF) | (((int) (Color.alpha(color) * 0.45f)) << 24);
        paint.setShader(new RadialGradient(cx, cy, radius,
                new int[]{color, mid, Color.TRANSPARENT},
                new float[]{0f, 0.5f, 1f}, Shader.TileMode.CLAMP));
        canvas.drawCircle(cx, cy, radius, paint);
        paint.setShader(null);
    }

    @Override
    public void setAlpha(int alpha) {
        paint.setAlpha(alpha);
    }

    @Override
    public void setColorFilter(android.graphics.ColorFilter cf) {
        paint.setColorFilter(cf);
    }

    @Override
    public int getOpacity() {
        return android.graphics.PixelFormat.TRANSLUCENT;
    }
}
