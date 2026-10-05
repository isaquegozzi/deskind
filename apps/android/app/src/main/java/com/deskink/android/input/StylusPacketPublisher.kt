package com.deskink.android.input

import android.os.Build
import android.view.MotionEvent
import com.deskink.android.protocol.InputBatchFrame
import com.deskink.android.protocol.PenSample
import com.deskink.android.protocol.ProtocolV1Codec
import com.deskink.android.protocol.SampleStateFlags
import com.deskink.android.protocol.ToolKind
import com.deskink.android.transport.InputTransport
import com.deskink.android.transport.TransportState
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit
import java.util.concurrent.RejectedExecutionHandler
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicInteger
import kotlin.math.PI
import kotlin.math.roundToInt

/** Copies MotionEvent data synchronously, then performs socket I/O off the UI thread. */
class StylusPacketPublisher(
    private val transportProvider: () -> InputTransport,
) : AutoCloseable {
    data class PerformanceSnapshot(
        val prepare: TimingPercentiles? = null,
        val encode: TimingPercentiles? = null,
        val queue: TimingPercentiles? = null,
        val send: TimingPercentiles? = null,
        val maximumQueueDepth: Int = 0,
    )

    private data class PointerState(
        var generation: Long = 0,
        var inRange: Boolean = false,
        var contact: Boolean = false,
        var tool: ToolKind = ToolKind.UNKNOWN,
        var buttons: Int = 0,
        var initialized: Boolean = false,
    )

    private data class SampleSnapshot(
        val timeUs: Long,
        val x: Float,
        val y: Float,
        val pressure: Float,
        val distance: Float,
        val tilt: Float,
        val orientation: Float,
        val buttons: Int,
        val flags: Int,
        val generation: Long,
        val critical: Boolean,
    )

    private val pointerStates = mutableMapOf<Int, PointerState>()
    private val droppedFrames = AtomicLong()
    private val maximumQueueDepth = AtomicInteger()
    private val prepareTiming = RollingTimingWindow(TIMING_WINDOW_CAPACITY)
    private val encodeTiming = RollingTimingWindow(TIMING_WINDOW_CAPACITY)
    private val queueTiming = RollingTimingWindow(TIMING_WINDOW_CAPACITY)
    private val sendTiming = RollingTimingWindow(TIMING_WINDOW_CAPACITY)
    @Volatile private var cachedPerformance = PerformanceSnapshot()
    @Volatile private var performanceSnapshotAtNs = 0L
    private var sequence = 0L
    private inner class SendTask(
        private val sessionId: Long,
        private val encoded: ByteArray,
        val critical: Boolean,
        private val enqueuedAtNs: Long = System.nanoTime(),
    ) : Runnable {
        override fun run() {
            queueTiming.record(System.nanoTime() - enqueuedAtNs)
            val transport = transportProvider()
            if (transport.state == TransportState.CONNECTED && transport.sessionId == sessionId) {
                val sendStarted = System.nanoTime()
                runCatching { transport.sendInput(encoded) }
                sendTiming.record(System.nanoTime() - sendStarted)
            }
        }
    }

    private val sender = ThreadPoolExecutor(
        1,
        1,
        0L,
        TimeUnit.MILLISECONDS,
        ArrayBlockingQueue(SEND_QUEUE_CAPACITY),
        { runnable -> Thread(runnable, "DeskInk-USB-Sender").apply { isDaemon = true } },
        RejectedExecutionHandler { task, executor ->
            val replaceable = executor.queue.firstOrNull {
                it is SendTask && !it.critical
            }
            when {
                replaceable != null -> {
                    executor.queue.remove(replaceable)
                    droppedFrames.incrementAndGet()
                    if (!executor.isShutdown) executor.execute(task)
                }
                task is SendTask && task.critical -> task.run()
                else -> droppedFrames.incrementAndGet()
            }
        },
    )

    fun publish(event: MotionEvent, surfaceWidth: Int, surfaceHeight: Int) {
        val transport = transportProvider()
        val sessionId = transport.sessionId
        if (transport.state != TransportState.CONNECTED || sessionId == 0L) return
        val prepareStarted = System.nanoTime()

        val capabilities = currentCapabilities ?: StylusCapabilities()
        for (pointerIndex in 0 until event.pointerCount) {
            val tool = toolKind(event.getToolType(pointerIndex)) ?: continue
            val pointerId = event.getPointerId(pointerIndex)
            val samples = ArrayList<SampleSnapshot>(event.historySize + 1)
            val historicalAction = StylusInputEngine.historicalActionFor(event.actionMasked)
            for (historyIndex in 0 until event.historySize) {
                samples += snapshot(
                    event,
                    pointerIndex,
                    historyIndex,
                    historicalAction,
                    tool,
                    capabilities,
                )
            }
            samples += snapshot(
                event,
                pointerIndex,
                historyIndex = null,
                action = StylusInputEngine.semanticActionFor(
                    event.actionMasked,
                    pointerIndex,
                    event.actionIndex,
                ),
                tool,
                capabilities,
            )

            samples.chunked(ProtocolV1Codec.MAXIMUM_SAMPLES_PER_BATCH).forEach { chunk ->
                val baseTimeUs = chunk.first().timeUs
                val frame = InputBatchFrame(
                    sessionId = sessionId,
                    sequence = nextSequence(),
                    baseMonotonicTimeUs = baseTimeUs,
                    toolKind = tool,
                    pointerId = pointerId,
                    samples = chunk.map { sample ->
                        PenSample(
                            deltaTimeUs = (sample.timeUs - baseTimeUs).coerceIn(0, UINT_MAX),
                            stateGeneration = sample.generation,
                            xNormalized = normalizeCoordinate(sample.x, surfaceWidth),
                            yNormalized = normalizeCoordinate(sample.y, surfaceHeight),
                            pressureNormalized = normalizeAxis(sample.pressure, capabilities.pressure),
                            distanceNormalized = normalizeAxis(sample.distance, capabilities.distance),
                            tiltCentidegrees = radiansToCentidegrees(sample.tilt, 0, 9000),
                            orientationCentidegrees = radiansToCentidegrees(
                                sample.orientation,
                                -18000,
                                18000,
                            ),
                            buttons = sample.buttons and USHORT_MAX,
                            stateFlags = sample.flags,
                        )
                    },
                )
                val encodeStarted = System.nanoTime()
                val encoded = ProtocolV1Codec.encodeInputBatch(frame)
                encodeTiming.record(System.nanoTime() - encodeStarted)
                sender.execute(SendTask(sessionId, encoded, chunk.any { it.critical }))
                maximumQueueDepth.updateAndGet { maximum -> maxOf(maximum, sender.queue.size) }
            }
        }
        prepareTiming.record(System.nanoTime() - prepareStarted)
    }

    fun droppedFrameCount(): Long = droppedFrames.get()

    fun performanceSnapshot(): PerformanceSnapshot {
        val now = System.nanoTime()
        if (now - performanceSnapshotAtNs < PERFORMANCE_REFRESH_NS) return cachedPerformance
        val snapshot = PerformanceSnapshot(
            prepare = prepareTiming.percentiles(),
            encode = encodeTiming.percentiles(),
            queue = queueTiming.percentiles(),
            send = sendTiming.percentiles(),
            maximumQueueDepth = maximumQueueDepth.get(),
        )
        cachedPerformance = snapshot
        performanceSnapshotAtNs = now
        return snapshot
    }

    override fun close() {
        sender.queue.clear()
        sender.shutdownNow()
        pointerStates.clear()
    }

    private fun snapshot(
        event: MotionEvent,
        pointerIndex: Int,
        historyIndex: Int?,
        action: Int,
        tool: ToolKind,
        capabilities: StylusCapabilities,
    ): SampleSnapshot {
        val pressure = axisValue(event, MotionEvent.AXIS_PRESSURE, pointerIndex, historyIndex)
        val state = pointerStates.getOrPut(event.getPointerId(pointerIndex)) { PointerState() }
        val canceledByPalm = Build.VERSION.SDK_INT >= 33 &&
            event.flags and MotionEvent.FLAG_CANCELED != 0 && historyIndex == null
        val (inRange, contact) = sampleState(action, pressure, state)
        val buttons = event.buttonState
        val stateChanged =
            !state.initialized || state.inRange != inRange || state.contact != contact ||
            state.tool != tool || state.buttons != buttons
        if (stateChanged) {
            state.generation = (state.generation + 1) and UINT_MAX
        }
        state.initialized = true
        state.inRange = inRange
        state.contact = contact
        state.tool = tool
        state.buttons = buttons

        var flags = 0
        if (inRange) flags = flags or SampleStateFlags.IN_RANGE
        if (contact) flags = flags or SampleStateFlags.CONTACT
        if (axisUsable(capabilities.pressure)) flags = flags or SampleStateFlags.PRESSURE_VALID
        if (axisUsable(capabilities.distance)) flags = flags or SampleStateFlags.DISTANCE_VALID
        if (capabilities.tilt.state == CapabilityState.SUPPORTED) {
            flags = flags or SampleStateFlags.TILT_VALID
        }
        if (capabilities.orientation.state == CapabilityState.SUPPORTED) {
            flags = flags or SampleStateFlags.ORIENTATION_VALID
        }
        if (historyIndex != null) flags = flags or SampleStateFlags.HISTORICAL
        if (action == MotionEvent.ACTION_CANCEL || canceledByPalm) {
            flags = (flags and (SampleStateFlags.IN_RANGE or SampleStateFlags.CONTACT).inv()) or
                SampleStateFlags.CANCELED
            state.inRange = false
            state.contact = false
        }

        return SampleSnapshot(
            timeUs = eventTimeUs(event, historyIndex),
            x = coordinate(event, pointerIndex, historyIndex, xAxis = true),
            y = coordinate(event, pointerIndex, historyIndex, xAxis = false),
            pressure = pressure,
            distance = axisValue(event, MotionEvent.AXIS_DISTANCE, pointerIndex, historyIndex),
            tilt = axisValue(event, MotionEvent.AXIS_TILT, pointerIndex, historyIndex),
            orientation = axisValue(event, MotionEvent.AXIS_ORIENTATION, pointerIndex, historyIndex),
            buttons = buttons,
            flags = flags,
            generation = state.generation,
            critical = stateChanged || action == MotionEvent.ACTION_CANCEL || canceledByPalm,
        )
    }

    private var currentCapabilities: StylusCapabilities? = null

    fun updateCapabilities(capabilities: StylusCapabilities) {
        currentCapabilities = capabilities
    }

    private fun nextSequence(): Long {
        val current = sequence
        sequence = (sequence + 1) and UINT_MAX
        return current
    }

    companion object {
        private const val SEND_QUEUE_CAPACITY = 256
        private const val TIMING_WINDOW_CAPACITY = 4096
        private const val PERFORMANCE_REFRESH_NS = 1_000_000_000L
        private const val USHORT_MAX = 0xffff
        private const val UINT_MAX = 0xffff_ffffL

        internal fun normalizeCoordinate(value: Float, extent: Int): Int {
            if (!value.isFinite() || extent <= 1) return 0
            return ((value / (extent - 1f)).coerceIn(0f, 1f) * USHORT_MAX).roundToInt()
        }

        internal fun normalizeAxis(value: Float, capability: AxisCapability): Int {
            val minimum = capability.minimum
            val maximum = capability.maximum
            if (!value.isFinite() || !axisUsable(capability) || minimum == null || maximum == null) {
                return 0
            }
            return (((value - minimum) / (maximum - minimum)).coerceIn(0f, 1f) * USHORT_MAX)
                .roundToInt()
        }

        internal fun radiansToCentidegrees(value: Float, minimum: Int, maximum: Int): Int {
            if (!value.isFinite()) return 0
            return (value * 180.0 / PI * 100.0).roundToInt().coerceIn(minimum, maximum)
        }

        private fun axisUsable(capability: AxisCapability): Boolean =
            capability.state == CapabilityState.SUPPORTED &&
                capability.minimum?.isFinite() == true &&
                capability.maximum?.isFinite() == true &&
                capability.maximum > capability.minimum

        private fun toolKind(toolType: Int): ToolKind? = when (toolType) {
            MotionEvent.TOOL_TYPE_FINGER -> ToolKind.FINGER
            MotionEvent.TOOL_TYPE_STYLUS -> ToolKind.STYLUS
            MotionEvent.TOOL_TYPE_ERASER -> ToolKind.ERASER
            else -> null
        }

        private fun sampleState(
            action: Int,
            pressure: Float,
            prior: PointerState,
        ): Pair<Boolean, Boolean> = when (action) {
            MotionEvent.ACTION_DOWN,
            MotionEvent.ACTION_POINTER_DOWN,
            MotionEvent.ACTION_MOVE,
            -> true to true
            MotionEvent.ACTION_UP,
            MotionEvent.ACTION_POINTER_UP,
            -> true to false
            MotionEvent.ACTION_HOVER_ENTER,
            MotionEvent.ACTION_HOVER_MOVE,
            -> true to false
            MotionEvent.ACTION_HOVER_EXIT,
            MotionEvent.ACTION_CANCEL,
            -> false to false
            MotionEvent.ACTION_BUTTON_PRESS,
            MotionEvent.ACTION_BUTTON_RELEASE,
            -> if (prior.initialized) prior.inRange to prior.contact else true to (pressure > 0f)
            else -> prior.inRange to prior.contact
        }

        private fun coordinate(
            event: MotionEvent,
            pointerIndex: Int,
            historyIndex: Int?,
            xAxis: Boolean,
        ): Float = when {
            historyIndex != null && xAxis -> event.getHistoricalX(pointerIndex, historyIndex)
            historyIndex != null -> event.getHistoricalY(pointerIndex, historyIndex)
            xAxis -> event.getX(pointerIndex)
            else -> event.getY(pointerIndex)
        }

        private fun axisValue(
            event: MotionEvent,
            axis: Int,
            pointerIndex: Int,
            historyIndex: Int?,
        ): Float = if (historyIndex == null) {
            event.getAxisValue(axis, pointerIndex)
        } else {
            event.getHistoricalAxisValue(axis, pointerIndex, historyIndex)
        }

        private fun eventTimeUs(event: MotionEvent, historyIndex: Int?): Long = when {
            historyIndex != null && Build.VERSION.SDK_INT >= 34 ->
                event.getHistoricalEventTimeNanos(historyIndex) / 1_000L
            historyIndex != null -> event.getHistoricalEventTime(historyIndex) * 1_000L
            Build.VERSION.SDK_INT >= 34 -> event.eventTimeNanos / 1_000L
            else -> event.eventTime * 1_000L
        }
    }
}
