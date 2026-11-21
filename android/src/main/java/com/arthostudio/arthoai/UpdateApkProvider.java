package com.arthostudio.arthoai;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileNotFoundException;

public final class UpdateApkProvider extends ContentProvider {
    static final String MIME_TYPE = "application/vnd.android.package-archive";
    static final String APK_NAME = "ArthoAI-Update.apk";

    @Override public boolean onCreate() { return true; }
    @Override public String getType(Uri uri) { return MIME_TYPE; }

    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode) || !("/" + APK_NAME).equals(uri.getEncodedPath())) {
            throw new FileNotFoundException();
        }
        return ParcelFileDescriptor.open(apk(), ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String order) {
        if (!("/" + APK_NAME).equals(uri.getEncodedPath())) return null;
        String[] columns = projection == null
                ? new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE} : projection;
        MatrixCursor cursor = new MatrixCursor(columns);
        MatrixCursor.RowBuilder row = cursor.newRow();
        for (String column : columns) {
            if (OpenableColumns.DISPLAY_NAME.equals(column)) row.add(APK_NAME);
            else if (OpenableColumns.SIZE.equals(column)) row.add(apk().length());
            else row.add(null);
        }
        return cursor;
    }

    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new UnsupportedOperationException(); }

    private File apk() { return new File(getContext().getCacheDir(), APK_NAME); }
}
