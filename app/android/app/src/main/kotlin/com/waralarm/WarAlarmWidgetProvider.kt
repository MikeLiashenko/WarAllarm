package com.waralarm

import android.appwidget.AppWidgetManager
import android.content.Context
import android.graphics.Color
import android.widget.RemoteViews
import es.antonborri.home_widget.HomeWidgetLaunchIntent
import es.antonborri.home_widget.HomeWidgetProvider
import android.content.SharedPreferences

/// Home-screen widget: shows the home status, attack probability and last
/// update. Data is written from Flutter (HomeWidgetService); tapping opens the
/// app.
class WarAlarmWidgetProvider : HomeWidgetProvider() {
    override fun onUpdate(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetIds: IntArray,
        widgetData: SharedPreferences
    ) {
        for (id in appWidgetIds) {
            val views = RemoteViews(context.packageName, R.layout.waralarm_widget)

            val level = widgetData.getString("w_level", "calm") ?: "calm"
            val title = widgetData.getString("w_title", "Спокійно") ?: "Спокійно"
            val place = widgetData.getString("w_place", "Дім") ?: "Дім"
            val percent = widgetData.getString("w_percent", "0") ?: "0"
            val detail = widgetData.getString("w_detail", "Очікую дані…") ?: ""
            val updated = widgetData.getString("w_updated", "WarAlarm") ?: "WarAlarm"

            val color = when (level) {
                "alarm" -> Color.parseColor("#FF453A")
                "warn" -> Color.parseColor("#F4C550")
                else -> Color.parseColor("#34D399")
            }

            views.setTextViewText(R.id.w_title, title)
            views.setTextColor(R.id.w_title, color)
            views.setTextViewText(R.id.w_percent, "$percent%")
            views.setTextColor(R.id.w_percent, color)
            views.setTextViewText(R.id.w_place, "📍 $place")
            views.setTextViewText(R.id.w_detail, detail)
            views.setTextViewText(R.id.w_updated, updated)

            // Tap anywhere → open the app.
            val pending = HomeWidgetLaunchIntent.getActivity(
                context, MainActivity::class.java
            )
            views.setOnClickPendingIntent(R.id.widget_root, pending)

            appWidgetManager.updateAppWidget(id, views)
        }
    }
}
