package com.arthostudio.arthoai;

import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.RadialGradient;
import android.graphics.Rect;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;

final class GlassBackdrop extends Drawable {
    private final Paint paint = new Paint();
    private Shader base;
    private Shader blue;
    private Shader violet;

    @Override protected void onBoundsChange(Rect bounds) {
        float width = Math.max(1, bounds.width());
        float height = Math.max(1, bounds.height());
        base = new LinearGradient(bounds.left, bounds.top, bounds.right, bounds.bottom,
                new int[]{0xff0b1729, 0xff10192c, 0xff151428}, null, Shader.TileMode.CLAMP);
        blue = new RadialGradient(bounds.left + width * .82f, bounds.top + height * .13f, height * .62f,
                new int[]{0x90446b9b, 0x38355883, 0x00355883}, null, Shader.TileMode.CLAMP);
        violet = new RadialGradient(bounds.left + width * .15f, bounds.top + height * .92f, height * .48f,
                0x624b3f7a, 0x004b3f7a, Shader.TileMode.CLAMP);
    }

    @Override public void draw(Canvas canvas) {
        paint.setShader(base);
        canvas.drawRect(getBounds(), paint);
        paint.setShader(blue);
        canvas.drawRect(getBounds(), paint);
        paint.setShader(violet);
        canvas.drawRect(getBounds(), paint);
    }

    @Override public void setAlpha(int alpha) { paint.setAlpha(alpha); invalidateSelf(); }
    @Override public void setColorFilter(ColorFilter filter) { paint.setColorFilter(filter); invalidateSelf(); }
    @Override public int getOpacity() { return PixelFormat.OPAQUE; }
}
