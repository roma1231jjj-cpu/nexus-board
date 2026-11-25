package ru.nexus.board;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.util.AttributeSet;
import android.view.View;

import java.util.ArrayList;
import java.util.List;

/** Мини-график loss без библиотек: ломаная плюс заливка под ней. */
public class SparkView extends View {

    private final List<Float> data = new ArrayList<>();
    private final Paint line = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint dot = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path path = new Path();
    private final Path area = new Path();

    public SparkView(Context c) {
        this(c, null);
    }

    public SparkView(Context c, AttributeSet a) {
        super(c, a);
        line.setStyle(Paint.Style.STROKE);
        line.setStrokeWidth(dp(2f));
        line.setStrokeJoin(Paint.Join.ROUND);
        line.setStrokeCap(Paint.Cap.ROUND);
        fill.setStyle(Paint.Style.FILL);
        dot.setStyle(Paint.Style.FILL);
        int accent = NxBackdrop.color(c, R.attr.nxPrimary);
        line.setColor(accent);
        dot.setColor(accent);
        fill.setColor((accent & 0x00FFFFFF) | 0x22000000);
    }

    private float dp(float v) {
        return v * getResources().getDisplayMetrics().density;
    }

    public void setData(List<Float> values) {
        data.clear();
        if (values != null) data.addAll(values);
        invalidate();
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (data.size() < 2) return;

        float pad = dp(6f);
        float w = getWidth() - pad * 2f;
        float h = getHeight() - pad * 2f;
        if (w <= 0 || h <= 0) return;

        float min = Float.MAX_VALUE, max = -Float.MAX_VALUE;
        for (Float v : data) {
            if (v == null) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (max - min < 0.0001f) {
            max = min + 0.01f;
        }

        path.reset();
        area.reset();
        float stepX = w / (data.size() - 1);
        for (int i = 0; i < data.size(); i++) {
            float v = data.get(i) == null ? min : data.get(i);
            float x = pad + stepX * i;
            float y = pad + h - (v - min) / (max - min) * h;
            if (i == 0) {
                path.moveTo(x, y);
                area.moveTo(x, pad + h);
                area.lineTo(x, y);
            } else {
                path.lineTo(x, y);
                area.lineTo(x, y);
            }
            if (i == data.size() - 1) {
                area.lineTo(x, pad + h);
                area.close();
                canvas.drawPath(area, fill);
                canvas.drawCircle(x, y, dp(3.2f), dot);
            }
        }
        canvas.drawPath(path, line);
    }
}
