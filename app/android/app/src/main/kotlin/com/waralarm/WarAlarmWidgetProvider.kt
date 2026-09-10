package com.waralarm

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context
import android.widget.RemoteViews

/// Minimal home-screen widget provider. The dynamic home_widget bridge was
/// removed (it crashed at launch on some devices), so this just shows the app's
/// static card; tapping is handled by the launcher.
class WarAlarmWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetIds: IntArray
    ) {
        for (id in appWidgetIds) {
            val views = RemoteViews(context.packageName, R.layout.waralarm_widget)
            appWidgetManager.updateAppWidget(id, views)
        }
    }
}
