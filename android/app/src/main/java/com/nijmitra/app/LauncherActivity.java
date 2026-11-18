package com.nijmitra.app;

import android.content.pm.ActivityInfo;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;

/**
 * Opens NijMitra (https://nijmitra.com) full screen as a Trusted Web Activity.
 * Everything the user sees is the same secure web app; this class only launches it.
 */
public class LauncherActivity extends com.google.androidbrowserhelper.trusted.LauncherActivity {

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        // Android 8.0 does not allow fixing the orientation of a translucent activity; keep it unspecified there.
        if (Build.VERSION.SDK_INT > Build.VERSION_CODES.O) {
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_USER);
        } else {
            setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED);
        }
    }

    @Override
    protected Uri getLaunchingUrl() {
        // Deep links (e.g. a notification or a shared nijmitra.com link) open that page; otherwise the start page.
        return super.getLaunchingUrl();
    }
}
