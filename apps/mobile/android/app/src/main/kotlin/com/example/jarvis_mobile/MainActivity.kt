package com.example.jarvis_mobile

import android.app.NotificationChannel
import android.app.NotificationManager
import android.os.Build
import io.flutter.embedding.android.FlutterActivity

class MainActivity : FlutterActivity() {
    override fun onCreate(savedInstanceState: android.os.Bundle?) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                "jarvis_notifications",
                "Jarvis notifications",
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = "Reminders, approvals, and task updates from Jarvis"
            }
            getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
        }
        super.onCreate(savedInstanceState)
    }
}
