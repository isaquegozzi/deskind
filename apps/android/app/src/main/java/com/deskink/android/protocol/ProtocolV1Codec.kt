package com.deskink.android.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

object ProtocolV1Codec {
    const val HEADER_BYTES = 32
    const val INPUT_PREFIX_BYTES = 4
    const val PEN_SAMPLE_BYTES = 24
    const val MAXIMUM_INPUT_FRAME_BYTES = 1200
    const val MAXIMUM_SAMPLES_PER_BATCH = 32

    private const val MAGIC = 0x494B5344
    private const val MAJOR = 1
    private const val MINOR = 0
    private const val INPUT_BATCH_TYPE = 0x10

    fun encodeInputBatch(frame: InputBatchFrame): ByteArray {
        validateFrame(frame)
        val frameBytes = HEADER_BYTES + INPUT_PREFIX_BYTES + frame.samples.size * PEN_SAMPLE_BYTES
        val buffer = ByteBuffer.allocate(frameBytes).order(ByteOrder.LITTLE_ENDIAN)

        buffer.putInt(MAGIC)
        buffer.put(MAJOR.toByte())
        buffer.put(MINOR.toByte())
        buffer.put(INPUT_BATCH_TYPE.toByte())
        buffer.put(0)
        buffer.putShort(HEADER_BYTES.toShort())
        buffer.putShort(frameBytes.toShort())
        buffer.putLong(frame.sessionId)
        buffer.putInt(frame.sequence.toInt())
        buffer.putLong(frame.baseMonotonicTimeUs)

        buffer.put(frame.samples.size.toByte())
        buffer.put(frame.toolKind.wireValue.toByte())
        buffer.putShort(frame.pointerId.toShort())
        frame.samples.forEach { sample -> writeSample(buffer, sample) }
        return buffer.array()
    }

    fun decodeInputBatch(bytes: ByteArray): InputBatchFrame {
        if (bytes.size !in (HEADER_BYTES + INPUT_PREFIX_BYTES + PEN_SAMPLE_BYTES)..MAXIMUM_INPUT_FRAME_BYTES) {
            throw ProtocolException("Input frame length is outside limits: ${bytes.size}")
        }
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        if (buffer.int != MAGIC) throw ProtocolException("Bad protocol magic")
        val major = buffer.unsignedByte()
        val minor = buffer.unsignedByte()
        if (major != MAJOR || minor != MINOR) {
            throw ProtocolException("Unsupported protocol version: $major.$minor")
        }
        if (buffer.unsignedByte() != INPUT_BATCH_TYPE) throw ProtocolException("Not an INPUT_BATCH")
        if (buffer.unsignedByte() != 0) throw ProtocolException("Unknown header flags")
        if (buffer.unsignedShort() != HEADER_BYTES) throw ProtocolException("Unexpected header size")
        if (buffer.unsignedShort() != bytes.size) throw ProtocolException("Frame length mismatch")

        val sessionId = buffer.long
        val sequence = Integer.toUnsignedLong(buffer.int)
        val baseTime = buffer.long
        if (baseTime < 0) throw ProtocolException("Negative monotonic timestamp")

        val sampleCount = buffer.unsignedByte()
        if (sampleCount !in 1..MAXIMUM_SAMPLES_PER_BATCH) {
            throw ProtocolException("Invalid sample count: $sampleCount")
        }
        val tool = ToolKind.fromWire(buffer.unsignedByte())
        val pointerId = buffer.unsignedShort()
        val expectedBytes = HEADER_BYTES + INPUT_PREFIX_BYTES + sampleCount * PEN_SAMPLE_BYTES
        if (expectedBytes != bytes.size) throw ProtocolException("Sample count/length mismatch")

        val samples = ArrayList<PenSample>(sampleCount)
        repeat(sampleCount) {
            samples += readSample(buffer)
        }
        return InputBatchFrame(sessionId, sequence, baseTime, tool, pointerId, samples)
    }

    private fun writeSample(buffer: ByteBuffer, sample: PenSample) {
        validateSample(sample)
        buffer.putInt(sample.deltaTimeUs.toInt())
        buffer.putInt(sample.stateGeneration.toInt())
        buffer.putShort(sample.xNormalized.toShort())
        buffer.putShort(sample.yNormalized.toShort())
        buffer.putShort(sample.pressureNormalized.toShort())
        buffer.putShort(sample.distanceNormalized.toShort())
        buffer.putShort(sample.tiltCentidegrees.toShort())
        buffer.putShort(sample.orientationCentidegrees.toShort())
        buffer.putShort(sample.buttons.toShort())
        buffer.putShort(sample.stateFlags.toShort())
    }

    private fun readSample(buffer: ByteBuffer): PenSample {
        val sample = PenSample(
            deltaTimeUs = Integer.toUnsignedLong(buffer.int),
            stateGeneration = Integer.toUnsignedLong(buffer.int),
            xNormalized = buffer.unsignedShort(),
            yNormalized = buffer.unsignedShort(),
            pressureNormalized = buffer.unsignedShort(),
            distanceNormalized = buffer.unsignedShort(),
            tiltCentidegrees = buffer.unsignedShort(),
            orientationCentidegrees = buffer.short.toInt(),
            buttons = buffer.unsignedShort(),
            stateFlags = buffer.unsignedShort(),
        )
        validateSample(sample)
        return sample
    }

    private fun validateFrame(frame: InputBatchFrame) {
        if (frame.sequence !in 0..UINT_MAX) throw ProtocolException("Sequence outside u32")
        if (frame.baseMonotonicTimeUs < 0) throw ProtocolException("Negative monotonic timestamp")
        if (frame.pointerId !in 0..USHORT_MAX) throw ProtocolException("Pointer id outside u16")
        if (frame.samples.size !in 1..MAXIMUM_SAMPLES_PER_BATCH) {
            throw ProtocolException("Invalid sample count: ${frame.samples.size}")
        }
        val bytes = HEADER_BYTES + INPUT_PREFIX_BYTES + frame.samples.size * PEN_SAMPLE_BYTES
        if (bytes > MAXIMUM_INPUT_FRAME_BYTES) throw ProtocolException("Input frame exceeds limit")
    }

    private fun validateSample(sample: PenSample) {
        requireUnsigned(sample.deltaTimeUs, "deltaTimeUs")
        requireUnsigned(sample.stateGeneration, "stateGeneration")
        requireUShort(sample.xNormalized, "xNormalized")
        requireUShort(sample.yNormalized, "yNormalized")
        requireUShort(sample.pressureNormalized, "pressureNormalized")
        requireUShort(sample.distanceNormalized, "distanceNormalized")
        requireUShort(sample.buttons, "buttons")
        if (sample.stateFlags and SampleStateFlags.KNOWN_MASK.inv() != 0) {
            throw ProtocolException("Unknown sample flags")
        }
        if (
            sample.stateFlags and SampleStateFlags.CONTACT != 0 &&
            sample.stateFlags and SampleStateFlags.IN_RANGE == 0
        ) {
            throw ProtocolException("CONTACT requires IN_RANGE")
        }
        if (sample.tiltCentidegrees !in 0..9000) throw ProtocolException("Tilt outside range")
        if (sample.orientationCentidegrees !in -18000..18000) {
            throw ProtocolException("Orientation outside range")
        }
    }

    private fun requireUnsigned(value: Long, name: String) {
        if (value !in 0..UINT_MAX) throw ProtocolException("$name outside u32")
    }

    private fun requireUShort(value: Int, name: String) {
        if (value !in 0..USHORT_MAX) throw ProtocolException("$name outside u16")
    }

    private fun ByteBuffer.unsignedByte(): Int = get().toInt() and 0xff
    private fun ByteBuffer.unsignedShort(): Int = short.toInt() and 0xffff

    private const val UINT_MAX = 0xffff_ffffL
    private const val USHORT_MAX = 0xffff
}
