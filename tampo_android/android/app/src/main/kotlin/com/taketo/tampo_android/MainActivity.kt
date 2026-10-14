package com.taketo.tampo_android

import android.media.AudioAttributes
import android.media.MediaPlayer
import android.os.Handler
import android.os.Looper
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File
import java.io.FileInputStream

class MainActivity : FlutterActivity() {
    private val CHANNEL = "com.taketo.tampo_android/audio"
    private val mainHandler = Handler(Looper.getMainLooper())
    private var mediaPlayer: MediaPlayer? = null
    private var pendingTimeoutRunnable: Runnable? = null
    private var pendingResult: MethodChannel.Result? = null

    private fun finishPendingResult(success: Boolean) {
        pendingTimeoutRunnable?.let { mainHandler.removeCallbacks(it) }
        pendingTimeoutRunnable = null
        val res = pendingResult
        pendingResult = null
        if (res != null) {
            try {
                res.success(success)
            } catch (_: Exception) {
            }
        }
    }

    private fun safeCleanMediaPlayer() {
        finishPendingResult(false)
        try {
            mediaPlayer?.let { player ->
                player.setOnPreparedListener(null)
                player.setOnCompletionListener(null)
                player.setOnErrorListener(null)
                try {
                    if (player.isPlaying) {
                        player.stop()
                    }
                } catch (_: Exception) {
                }
                player.reset()
                player.release()
            }
        } catch (_: Exception) {
        }
        mediaPlayer = null
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, CHANNEL).setMethodCallHandler { call, result ->
            when (call.method) {
                "playUrl" -> {
                    val url = call.argument<String>("url")
                    if (url.isNullOrEmpty()) {
                        result.success(false)
                        return@setMethodCallHandler
                    }
                    try {
                        safeCleanMediaPlayer()
                        pendingResult = result

                        val newPlayer = MediaPlayer()
                        mediaPlayer = newPlayer

                        val timeoutRunnable = Runnable {
                            if (mediaPlayer === newPlayer) {
                                safeCleanMediaPlayer()
                            } else {
                                finishPendingResult(false)
                            }
                        }
                        pendingTimeoutRunnable = timeoutRunnable
                        mainHandler.postDelayed(timeoutRunnable, 4000)

                        newPlayer.apply {
                            setAudioAttributes(
                                AudioAttributes.Builder()
                                    .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                                    .setUsage(AudioAttributes.USAGE_MEDIA)
                                    .build()
                            )
                            if (url.startsWith("/")) {
                                val file = File(url)
                                if (file.exists()) {
                                    FileInputStream(file).use { fis ->
                                        setDataSource(fis.fd)
                                    }
                                } else {
                                    setDataSource(url)
                                }
                            } else {
                                setDataSource(url)
                            }
                            setOnPreparedListener { mp ->
                                if (mediaPlayer === mp) {
                                    finishPendingResult(true)
                                    try {
                                        mp.start()
                                    } catch (_: Exception) {
                                        safeCleanMediaPlayer()
                                    }
                                }
                            }
                            setOnErrorListener { mp, _, _ ->
                                if (mediaPlayer === mp) {
                                    safeCleanMediaPlayer()
                                }
                                true
                            }
                            setOnCompletionListener { mp ->
                                if (mediaPlayer === mp) {
                                    safeCleanMediaPlayer()
                                }
                            }
                            prepareAsync()
                        }
                    } catch (_: Exception) {
                        safeCleanMediaPlayer()
                    }
                }
                "stop" -> {
                    safeCleanMediaPlayer()
                    result.success(true)
                }
                else -> result.notImplemented()
            }
        }
    }

    override fun onDestroy() {
        safeCleanMediaPlayer()
        super.onDestroy()
    }
}
