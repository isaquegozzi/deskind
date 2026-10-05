package com.deskink.android.transport

class InMemoryInputTransport : InputTransport {
    private val inputFrames = mutableListOf<ByteArray>()
    private val controlFrames = mutableListOf<ByteArray>()

    override var state: TransportState = TransportState.DISCONNECTED
        private set

    override var metrics: TransportMetrics = TransportMetrics()
        private set

    override val lastError: String? = null
    override val sessionId: Long = 1
    override val monitorCount: Int = 1
    override val selectedMonitorIndex: Int = 0

    val capturedInputFrames: List<ByteArray>
        get() = inputFrames

    val capturedControlFrames: List<ByteArray>
        get() = controlFrames

    override fun connect() {
        state = TransportState.CONNECTED
    }

    override fun disconnect() {
        state = TransportState.DISCONNECTED
    }

    override fun sendInput(frame: ByteArray) {
        checkConnected()
        inputFrames += frame.copyOf()
        metrics = metrics.copy(
            inputFramesSent = metrics.inputFramesSent + 1,
            bytesSent = metrics.bytesSent + frame.size,
        )
    }

    override fun sendControl(frame: ByteArray) {
        checkConnected()
        controlFrames += frame.copyOf()
        metrics = metrics.copy(
            controlFramesSent = metrics.controlFramesSent + 1,
            bytesSent = metrics.bytesSent + frame.size,
        )
    }

    private fun checkConnected() {
        check(state == TransportState.CONNECTED) { "Transport is disconnected" }
    }
}
