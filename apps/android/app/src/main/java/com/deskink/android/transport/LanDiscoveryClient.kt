package com.deskink.android.transport

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import java.security.SecureRandom

data class DiscoveredDeskInkHost(
    val address: String,
    val controlPort: Int,
    val hostName: String,
    val comparisonCode: String,
    val certificateFingerprintHex: String,
)

class LanDiscoveryClient(
    private val discoveryPort: Int = 27187,
) {
    fun discover(timeoutMilliseconds: Int = 1_500): List<DiscoveredDeskInkHost> {
        require(timeoutMilliseconds in 100..10_000)
        val nonce = SecureRandom().nextLong()
        val query = LanDiscoveryProtocol.encodeQuery(nonce)
        val results = linkedMapOf<String, DiscoveredDeskInkHost>()
        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = 200
            broadcastAddresses().forEach { address ->
                runCatching {
                    socket.send(DatagramPacket(query, query.size, InetSocketAddress(address, discoveryPort)))
                }
            }
            val deadline = System.nanoTime() + timeoutMilliseconds * 1_000_000L
            while (System.nanoTime() < deadline) {
                val packet = DatagramPacket(ByteArray(MAXIMUM_REPLY_BYTES), MAXIMUM_REPLY_BYTES)
                try {
                    socket.receive(packet)
                    val reply = LanDiscoveryProtocol.decodeReply(packet.data.copyOf(packet.length))
                    if (reply.nonce != nonce) continue
                    val address = packet.address.hostAddress ?: continue
                    results["$address:${reply.controlPort}"] = DiscoveredDeskInkHost(
                        address,
                        reply.controlPort,
                        reply.hostName,
                        LanTransport.comparisonCode(reply.certificateFingerprint),
                        LanTransport.hex(reply.certificateFingerprint),
                    )
                } catch (_: SocketTimeoutException) {
                    // Poll until the overall deadline so multiple hosts can reply.
                } catch (_: Exception) {
                    // Discovery is unauthenticated; ignore malformed network traffic.
                }
            }
        }
        return results.values.sortedWith(compareBy({ it.hostName }, { it.address }))
    }

    private fun broadcastAddresses(): Set<Inet4Address> {
        val addresses = linkedSetOf<Inet4Address>()
        addresses += Inet4Address.getByName("255.255.255.255") as Inet4Address
        NetworkInterface.getNetworkInterfaces()?.toList().orEmpty()
            .filter { it.isUp && !it.isLoopback }
            .flatMap { it.interfaceAddresses }
            .mapNotNull { it.broadcast as? Inet4Address }
            .forEach(addresses::add)
        return addresses
    }

    companion object {
        private const val MAXIMUM_REPLY_BYTES = 128
    }
}
