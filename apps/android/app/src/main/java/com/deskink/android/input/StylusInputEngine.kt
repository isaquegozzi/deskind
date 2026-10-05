package com.deskink.android.input

import android.os.Build
import android.view.InputDevice
import android.view.MotionEvent

/**
 * Copies raw MotionEvent telemetry into primitive fields. MotionEvent instances
 * are never retained because Android may recycle them after dispatch returns.
 */
class StylusInputEngine {
    private var sampleCount = 0L
    private var eventCount = 0L
    private var historicalSampleCount = 0L
    private var cancelCount = 0L

    private var pointerId = -1
    private var toolType = MotionEvent.TOOL_TYPE_UNKNOWN
    private var action = MotionEvent.ACTION_CANCEL
    private var x = 0f
    private var y = 0f
    private var pressure = 0f
    private var tiltRadians = 0f
    private var orientationRadians = 0f
    private var distance = 0f
    private var buttonState = 0
    private var actionButton = 0
    private var eventTimeNanos = 0L
    private var isHistorical = false
    private var isCanceledByPalm = false

    private var observedHover = false
    private var observedSideButton = false
    private var observedEraser = false
    private var capabilities = StylusCapabilities()
    private var capabilityDeviceId = Int.MIN_VALUE
    private var capabilitySource = Int.MIN_VALUE

    fun consume(event: MotionEvent) {
        eventCount += 1
        updateCapabilities(event)

        val historicalAction = historicalActionFor(event.actionMasked)
        for (historyIndex in 0 until event.historySize) {
            for (pointerIndex in 0 until event.pointerCount) {
                recordHistorical(event, pointerIndex, historyIndex, historicalAction)
            }
        }

        for (pointerIndex in 0 until event.pointerCount) {
            recordCurrent(
                event = event,
                pointerIndex = pointerIndex,
                semanticAction = semanticActionFor(
                    event.actionMasked,
                    pointerIndex,
                    event.actionIndex,
                ),
            )
        }

        if (event.actionMasked == MotionEvent.ACTION_CANCEL) {
            cancelCount += 1
        }
    }

    fun snapshot(): StylusTelemetrySnapshot = StylusTelemetrySnapshot(
        sampleCount = sampleCount,
        eventCount = eventCount,
        historicalSampleCount = historicalSampleCount,
        cancelCount = cancelCount,
        pointerId = pointerId,
        toolType = toolType,
        action = action,
        x = x,
        y = y,
        pressure = pressure,
        tiltRadians = tiltRadians,
        orientationRadians = orientationRadians,
        distance = distance,
        buttonState = buttonState,
        actionButton = actionButton,
        eventTimeNanos = eventTimeNanos,
        isHistorical = isHistorical,
        isCanceledByPalm = isCanceledByPalm,
        capabilities = capabilities,
    )

    fun hasSamples(): Boolean = sampleCount != 0L

    fun lastX(): Float = x

    fun lastY(): Float = y

    fun currentCapabilities(): StylusCapabilities = capabilities

    private fun recordHistorical(
        event: MotionEvent,
        pointerIndex: Int,
        historyIndex: Int,
        semanticAction: Int,
    ) {
        sampleCount += 1
        historicalSampleCount += 1
        pointerId = event.getPointerId(pointerIndex)
        toolType = event.getToolType(pointerIndex)
        action = semanticAction
        x = event.getHistoricalX(pointerIndex, historyIndex)
        y = event.getHistoricalY(pointerIndex, historyIndex)
        pressure = event.getHistoricalPressure(pointerIndex, historyIndex)
        tiltRadians = event.getHistoricalAxisValue(
            MotionEvent.AXIS_TILT,
            pointerIndex,
            historyIndex,
        )
        orientationRadians = event.getHistoricalAxisValue(
            MotionEvent.AXIS_ORIENTATION,
            pointerIndex,
            historyIndex,
        )
        distance = event.getHistoricalAxisValue(
            MotionEvent.AXIS_DISTANCE,
            pointerIndex,
            historyIndex,
        )
        buttonState = event.buttonState
        actionButton = 0
        eventTimeNanos = historicalEventTimeNanos(event, historyIndex)
        isHistorical = true
        isCanceledByPalm = false
        observeToolAndButtons(toolType, buttonState, semanticAction)
    }

    private fun recordCurrent(event: MotionEvent, pointerIndex: Int, semanticAction: Int) {
        sampleCount += 1
        pointerId = event.getPointerId(pointerIndex)
        toolType = event.getToolType(pointerIndex)
        action = semanticAction
        x = event.getX(pointerIndex)
        y = event.getY(pointerIndex)
        pressure = event.getPressure(pointerIndex)
        tiltRadians = event.getAxisValue(MotionEvent.AXIS_TILT, pointerIndex)
        orientationRadians = event.getAxisValue(MotionEvent.AXIS_ORIENTATION, pointerIndex)
        distance = event.getAxisValue(MotionEvent.AXIS_DISTANCE, pointerIndex)
        buttonState = event.buttonState
        actionButton = if (
            event.actionMasked == MotionEvent.ACTION_BUTTON_PRESS ||
            event.actionMasked == MotionEvent.ACTION_BUTTON_RELEASE
        ) {
            event.actionButton
        } else {
            0
        }
        eventTimeNanos = currentEventTimeNanos(event)
        isHistorical = false
        isCanceledByPalm = Build.VERSION.SDK_INT >= 33 &&
            event.flags and MotionEvent.FLAG_CANCELED != 0
        observeToolAndButtons(toolType, buttonState, semanticAction)
    }

    private fun observeToolAndButtons(tool: Int, buttons: Int, sampleAction: Int) {
        var capabilitiesChanged = false
        val isHover =
            sampleAction == MotionEvent.ACTION_HOVER_ENTER ||
            sampleAction == MotionEvent.ACTION_HOVER_MOVE ||
            sampleAction == MotionEvent.ACTION_HOVER_EXIT
        if (isHover && !observedHover) {
            observedHover = true
            capabilitiesChanged = true
        }
        if (buttons and STYLUS_BUTTON_MASK != 0 && !observedSideButton) {
            observedSideButton = true
            capabilitiesChanged = true
        }
        if (tool == MotionEvent.TOOL_TYPE_ERASER && !observedEraser) {
            observedEraser = true
            capabilitiesChanged = true
        }
        if (capabilitiesChanged) {
            capabilities = capabilities.copy(
                hover = observedState(observedHover, capabilities.hover),
                sideButton = observedState(observedSideButton, capabilities.sideButton),
                eraser = observedState(observedEraser, capabilities.eraser),
            )
        }
    }

    private fun updateCapabilities(event: MotionEvent) {
        if (event.deviceId == capabilityDeviceId && event.source == capabilitySource) {
            return
        }
        capabilityDeviceId = event.deviceId
        capabilitySource = event.source

        val device = event.device
        val stylusSource = device?.supportsSource(InputDevice.SOURCE_STYLUS) == true ||
            device?.supportsSource(InputDevice.SOURCE_BLUETOOTH_STYLUS) == true ||
            (0 until event.pointerCount).any {
                event.getToolType(it) == MotionEvent.TOOL_TYPE_STYLUS ||
                    event.getToolType(it) == MotionEvent.TOOL_TYPE_ERASER
            }

        capabilities = StylusCapabilities(
            deviceId = event.deviceId,
            descriptor = device?.descriptor ?: "unknown",
            deviceName = device?.name ?: "Unknown input device",
            pressure = axisCapability(device, MotionEvent.AXIS_PRESSURE, event.source, stylusSource),
            tilt = axisCapability(device, MotionEvent.AXIS_TILT, event.source, stylusSource),
            orientation = axisCapability(
                device,
                MotionEvent.AXIS_ORIENTATION,
                event.source,
                stylusSource,
            ),
            distance = axisCapability(device, MotionEvent.AXIS_DISTANCE, event.source, stylusSource),
            hover = observedState(observedHover, CapabilityState.UNKNOWN),
            sideButton = observedState(observedSideButton, CapabilityState.UNKNOWN),
            eraser = observedState(observedEraser, CapabilityState.UNKNOWN),
        )
    }

    private fun axisCapability(
        device: InputDevice?,
        axis: Int,
        source: Int,
        stylusSource: Boolean,
    ): AxisCapability {
        if (device == null) {
            return AxisCapability(CapabilityState.UNKNOWN)
        }
        val range = device.getMotionRange(axis, source)
            ?: device.getMotionRange(axis, InputDevice.SOURCE_STYLUS)
            ?: device.getMotionRange(axis, InputDevice.SOURCE_BLUETOOTH_STYLUS)
        if (range == null) {
            return AxisCapability(
                if (stylusSource) CapabilityState.UNSUPPORTED else CapabilityState.UNKNOWN,
            )
        }
        return AxisCapability(
            state = CapabilityState.SUPPORTED,
            minimum = range.min,
            maximum = range.max,
            resolution = range.resolution,
            fuzz = range.fuzz,
        )
    }

    companion object {
        private const val STYLUS_BUTTON_MASK =
            MotionEvent.BUTTON_STYLUS_PRIMARY or MotionEvent.BUTTON_STYLUS_SECONDARY

        internal fun semanticActionFor(maskedAction: Int, pointerIndex: Int, actionIndex: Int): Int {
            return when (maskedAction) {
                MotionEvent.ACTION_POINTER_DOWN,
                MotionEvent.ACTION_POINTER_UP,
                -> if (pointerIndex == actionIndex) maskedAction else MotionEvent.ACTION_MOVE
                else -> maskedAction
            }
        }

        internal fun historicalActionFor(maskedAction: Int): Int = when (maskedAction) {
            MotionEvent.ACTION_HOVER_ENTER,
            MotionEvent.ACTION_HOVER_MOVE,
            MotionEvent.ACTION_HOVER_EXIT,
            -> MotionEvent.ACTION_HOVER_MOVE
            else -> MotionEvent.ACTION_MOVE
        }

        private fun observedState(observed: Boolean, prior: CapabilityState): CapabilityState =
            if (observed) CapabilityState.SUPPORTED else prior

        private fun currentEventTimeNanos(event: MotionEvent): Long =
            if (Build.VERSION.SDK_INT >= 34) event.eventTimeNanos else event.eventTime * 1_000_000L

        private fun historicalEventTimeNanos(event: MotionEvent, historyIndex: Int): Long =
            if (Build.VERSION.SDK_INT >= 34) {
                event.getHistoricalEventTimeNanos(historyIndex)
            } else {
                event.getHistoricalEventTime(historyIndex) * 1_000_000L
            }
    }
}
