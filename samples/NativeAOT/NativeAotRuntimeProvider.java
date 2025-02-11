package net.dot.jni.nativeaot;

import android.util.Log;
import net.dot.android.ApplicationRegistration;

public class NativeAotRuntimeProvider
    extends android.content.ContentProvider
{
    private static final String TAG = "NativeAotRuntimeProvider";

    public NativeAotRuntimeProvider() {
        Log.d(TAG, "NativeAotRuntimeProvider()");
    }

    @Override
    public boolean onCreate() {
        Log.d(TAG, "NativeAotRuntimeProvider.onCreate()");
        return true;
    }

    @Override
    public void attachInfo(android.content.Context context, android.content.pm.ProviderInfo info) {
        Log.d(TAG, "NativeAotRuntimeProvider.attachInfo(): calling JavaInteropRuntime.init()…");
        if (context instanceof android.app.Application) {
            ApplicationRegistration.Context = context;
        }
        JavaInteropRuntime.init();
        // NOTE: only required for custom applications
        ApplicationRegistration.registerApplications();
        super.attachInfo (context, info);
    }

    @Override
    public android.database.Cursor query(android.net.Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        throw new RuntimeException ("This operation is not supported.");
    }

    @Override
    public String getType(android.net.Uri uri) {
        throw new RuntimeException ("This operation is not supported.");
    }

    @Override
    public android.net.Uri insert(android.net.Uri uri, android.content.ContentValues initialValues) {
        throw new RuntimeException ("This operation is not supported.");
    }

    @Override
    public int delete(android.net.Uri uri, String where, String[] whereArgs) {
        throw new RuntimeException ("This operation is not supported.");
    }

    @Override
    public int update(android.net.Uri uri, android.content.ContentValues values, String where, String[] whereArgs) {
        throw new RuntimeException ("This operation is not supported.");
    }
}