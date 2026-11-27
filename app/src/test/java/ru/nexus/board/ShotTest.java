package ru.nexus.board;

import android.app.Activity;
import android.app.Dialog;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.view.View;
import android.view.View.MeasureSpec;

import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.GraphicsMode;
import org.robolectric.shadows.ShadowDialog;
import org.robolectric.shadows.ShadowLooper;

import java.io.File;
import java.io.FileOutputStream;
import java.lang.reflect.Method;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 34, qualifiers = "w411dp-h891dp-xxhdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class ShotTest {

    private static final int W = 1080;
    private static final int H = 2340;

    private static Bitmap render(View root, int w, int h) {
        root.measure(MeasureSpec.makeMeasureSpec(w, MeasureSpec.EXACTLY),
                MeasureSpec.makeMeasureSpec(h, MeasureSpec.EXACTLY));
        root.layout(0, 0, w, h);
        Bitmap bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bmp);
        canvas.drawColor(0xFF101018);
        root.draw(canvas);
        return bmp;
    }

    private static void save(Bitmap bmp, String name) throws Exception {
        File dir = new File(System.getProperty("user.home"), "board-shots");
        dir.mkdirs();
        File out = new File(dir, name);
        try (FileOutputStream fos = new FileOutputStream(out)) {
            bmp.compress(Bitmap.CompressFormat.PNG, 100, fos);
        }
        System.out.println("SHOT " + out.getAbsolutePath() + " " + bmp.getWidth() + "x" + bmp.getHeight());
    }

    private static MainActivity launch() {
        Context ctx = RuntimeEnvironment.getApplication();
        ctx.getSharedPreferences("board", Context.MODE_PRIVATE).edit()
                .putString("theme", "midnight")
                .putString("lang", "en")
                .putString("base", "http://127.0.0.1:8788")
                .putString("boardId", "8408c3d6ff05aefd")
                .putString("key", "")
                .apply();
        MainActivity activity = Robolectric.buildActivity(MainActivity.class).setup().get();
        try { Thread.sleep(2500); } catch (InterruptedException ignored) { }
        ShadowLooper.idleMainLooper();
        return activity;
    }

    @Test
    public void boardScreenshot() throws Exception {
        MainActivity activity = launch();
        save(render(activity.getWindow().getDecorView(), W, H), "board-en.png");
    }

    @Test
    public void keyScreenshot() throws Exception {
        MainActivity activity = launch();
        Method open = MainActivity.class.getDeclaredMethod("openConnect");
        open.setAccessible(true);
        open.invoke(activity);
        ShadowLooper.idleMainLooper();
        Dialog dialog = ShadowDialog.getLatestDialog();
        if (dialog == null) throw new IllegalStateException("no dialog");
        Bitmap base = render(activity.getWindow().getDecorView(), W, H);
        Canvas canvas = new Canvas(base);
        Paint dim = new Paint();
        dim.setColor(Color.argb(150, 0, 0, 0));
        canvas.drawRect(0, 0, W, H, dim);
        View decor = dialog.getWindow().getDecorView();
        int dw = (int) (W * 0.88f);
        decor.measure(MeasureSpec.makeMeasureSpec(dw, MeasureSpec.EXACTLY),
                MeasureSpec.makeMeasureSpec(H, MeasureSpec.AT_MOST));
        int dh = decor.getMeasuredHeight();
        decor.layout(0, 0, dw, dh);
        canvas.translate((W - dw) / 2f, (H - dh) / 2f);
        decor.draw(canvas);
        save(base, "key-en.png");
    }
}