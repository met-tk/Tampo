package com.taketo.tampo_android

import android.media.AudioAttributes
import android.media.MediaPlayer
import android.os.Handler
import android.os.Looper
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    private val CHANNEL = "com.taketo.tampo_android/audio"
    private var mediaPlayer: MediaPlayer? = null

    private fun safeCleanMediaPlayer() {
        try {
            mediaPlayer?.let { player ->
                player.setOnPreparedListener(null)
                player.setOnCompletionListener(null)
                player.setOnErrorListener(null)
                if (player.isPlaying) {
                    player.stop()
                }
                player.reset()
                player.release()
            }
        } catch (_: Exception) {}
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
                        var hasResponded = false
                        val mainHandler = Handler(Looper.getMainLooper())

                        val timeoutRunnable = Runnable {
                            if (!hasResponded) {
                                hasResponded = true
                                safeCleanMediaPlayer()
                                result.success(false)
                            }
                        }
                        mainHandler.postDelayed(timeoutRunnable, 4000)

                        val newPlayer = MediaPlayer().apply {
                            setAudioAttributes(
                                AudioAttributes.Builder()
                                    .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                                    .setUsage(AudioAttributes.USAGE_MEDIA)
                                    .build()
                            )
                            if (url.startsWith("/")) {
                                val file = java.io.File(url)
                                if (file.exists()) {
                                    val fis = java.io.FileInputStream(file)
                                    setDataSource(fis.fd)
                                    fis.close()
                                } else {
                                    setDataSource(url)
                                }
                            } else {
                                setDataSource(url)
                            }
                            setOnPreparedListener { mp ->
                                if (mediaPlayer == mp) {
                                    if (!hasResponded) {
                                        hasResponded = true
                                        mainHandler.removeCallbacks(timeoutRunnable)
                                        result.success(true)
                                    }
                                    mp.start()
                                }
                            }
                            setOnErrorListener { mp, _, _ ->
                                if (mediaPlayer == mp) {
                                    safeCleanMediaPlayer()
                                }
                                if (!hasResponded) {
                                    hasResponded = true
                                    mainHandler.removeCallbacks(timeoutRunnable)
                                    result.success(false)
                                }
                                true
                            }
                            setOnCompletionListener { mp ->
                                if (mediaPlayer == mp) {
                                    safeCleanMediaPlayer()
                                }
                            }
                            prepareAsync()
                        }
                        mediaPlayer = newPlayer
                    } catch (e: Exception) {
                        safeCleanMediaPlayer()
                        result.success(false)
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
        mediaPlayer?.release()
        mediaPlayer = null
        super.onDestroy()
    }
}
