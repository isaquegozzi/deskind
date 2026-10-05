package com.deskink.android.ui

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.view.MotionEvent
import android.view.View
import com.deskink.android.R
import com.deskink.android.input.StylusInputEngine

class StylusCaptureView(
    context: Context,
    private val inputEngine: StylusInputEngine,
    private val eventObserver: ((MotionEvent, Int, Int) -> Unit)? = null,
    private val twoFingerTapObserver: (() -> Unit)? = null,
    private val unbufferedDispatchEnabled: (() -> Boolean)? = null,
) : View(context) {
    private val crosshairPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = context.getColor(R.color.deskink_accent)
        strokeWidth = 3f * resources.displayMetrics.density
        style = Paint.Style.STROKE
    }
    private val twoFingerTapDetector = TwoFingerTapDetector(
        maximumDurationMs = 350,
        maximumMovementPx = 28f * resources.displayMetrics.density,
    )

    init {
        setBackgroundColor(context.getColor(R.color.deskink_surface))
        contentDescription = context.getString(R.string.capture_surface_description)
        isFocusable = true
        isClickable = true
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        requestLowLatencyDispatch(event)
        observeTwoFingerTap(event)
        capture(event)
        if (event.actionMasked == MotionEvent.ACTION_UP) {
            performClick()
        }
        return true
    }

    override fun onHoverEvent(event: MotionEvent): Boolean {
        requestLowLatencyDispatch(event)
        capture(event)
        return true
    }

    override fun onGenericMotionEvent(event: MotionEvent): Boolean {
        return when (event.actionMasked) {
            MotionEvent.ACTION_BUTTON_PRESS,
            MotionEvent.ACTION_BUTTON_RELEASE,
            -> {
                capture(event)
                true
            }
            else -> super.onGenericMotionEvent(event)
        }
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        if (!inputEngine.hasSamples()) {
            return
        }
        val x = inputEngine.lastX()
        val y = inputEngine.lastY()
        val radius = 12f * resources.displayMetrics.density
        canvas.drawCircle(x, y, radius, crosshairPaint)
        canvas.drawLine(x - radius * 1.5f, y, x + radius * 1.5f, y, crosshairPaint)
        canvas.drawLine(x, y - radius * 1.5f, x, y + radius * 1.5f, crosshairPaint)
    }

    private fun capture(event: MotionEvent) {
        inputEngine.consume(event)
        eventObserver?.invoke(event, width, height)
        postInvalidateOnAnimation()
    }

    private fun requestLowLatencyDispatch(event: MotionEvent) {
        if (unbufferedDispatchEnabled?.invoke() != true) return
        if (event.actionMasked == MotionEvent.ACTION_DOWN ||
            event.actionMasked == MotionEvent.ACTION_HOVER_ENTER) {
            requestUnbufferedDispatch(event)
        }
    }

    private fun observeTwoFingerTap(event: MotionEvent) {
        val bothPointersAreFingers = event.pointerCount == 2 &&
            event.getToolType(0) == MotionEvent.TOOL_TYPE_FINGER &&
            event.getToolType(1) == MotionEvent.TOOL_TYPE_FINGER
        when (event.actionMasked) {
            MotionEvent.ACTION_POINTER_DOWN -> {
                if (bothPointersAreFingers) {
                    twoFingerTapDetector.begin(event.eventTime, centroidX(event), centroidY(event))
                } else {
                    twoFingerTapDetector.cancel()
                }
            }
            MotionEvent.ACTION_MOVE -> {
                if (bothPointersAreFingers) {
                    twoFingerTapDetector.move(centroidX(event), centroidY(event))
                } else {
                    twoFingerTapDetector.cancel()
                }
            }
            MotionEvent.ACTION_POINTER_UP -> {
                if (bothPointersAreFingers && twoFingerTapDetector.end(
                        event.eventTime,
                        centroidX(event),
                        centroidY(event),
                    )) {
                    twoFingerTapObserver?.invoke()
                }
            }
            MotionEvent.ACTION_CANCEL, MotionEvent.ACTION_UP -> twoFingerTapDetector.cancel()
        }
    }

    private fun centroidX(event: MotionEvent) = (event.getX(0) + event.getX(1)) / 2f

    private fun centroidY(event: MotionEvent) = (event.getY(0) + event.getY(1)) / 2f
}
