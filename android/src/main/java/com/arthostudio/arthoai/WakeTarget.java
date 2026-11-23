package com.arthostudio.arthoai;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.InetAddress;

final class WakeTarget {
    final String address;
    final String mac;
    final int prefix;
    final String name;

    WakeTarget(String address, String mac, int prefix, String name) {
        if (prefix < 1 || prefix > 30 || !privateAddress(ipv4(address))) throw new IllegalArgumentException("Rede inválida.");
        magicPacket(mac);
        this.address = address;
        this.mac = mac;
        this.prefix = prefix;
        this.name = name;
    }

    boolean samePc(WakeTarget other) {
        return address.equals(other.address) && mac.equalsIgnoreCase(other.mac) && prefix == other.prefix;
    }

    void send(Context context) throws IOException {
        ConnectivityManager networks = context.getSystemService(ConnectivityManager.class);
        int target = ipv4(address);
        int mask = mask(prefix);
        for (Network network : networks.getAllNetworks()) {
            NetworkCapabilities capabilities = networks.getNetworkCapabilities(network);
            if (capabilities == null || !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) continue;
            LinkProperties link = networks.getLinkProperties(network);
            if (link == null) continue;
            for (LinkAddress local : link.getLinkAddresses()) {
                if (!(local.getAddress() instanceof Inet4Address) || local.getPrefixLength() != prefix) continue;
                int phone = ipv4(local.getAddress().getHostAddress());
                if (phone == target || (phone & mask) != (target & mask)) continue;
                byte[] payload = magicPacket(mac);
                int broadcast = target | ~mask;
                InetAddress destination = InetAddress.getByAddress(new byte[] {
                        (byte) (broadcast >>> 24), (byte) (broadcast >>> 16), (byte) (broadcast >>> 8), (byte) broadcast
                });
                try (DatagramSocket socket = new DatagramSocket()) {
                    network.bindSocket(socket);
                    socket.setBroadcast(true);
                    sendPackets(socket, payload, destination,
                            InetAddress.getByAddress(new byte[]{-1, -1, -1, -1}), 9);
                    return;
                }
            }
        }
        throw new IOException("Conecte o celular ao Wi-Fi da mesma rede do PC.");
    }

    static void sendPackets(DatagramSocket socket, byte[] payload, InetAddress broadcast,
                            InetAddress fallback, int port) throws IOException {
        boolean sent = false;
        IOException failure = null;
        for (InetAddress destination : new InetAddress[]{broadcast, fallback, broadcast}) {
            try {
                socket.send(new DatagramPacket(payload, payload.length, destination, port));
                sent = true;
            } catch (IOException error) { failure = error; }
        }
        if (!sent && failure != null) throw failure;
    }

    static byte[] magicPacket(String mac) {
        if (mac == null || !mac.matches("[0-9a-fA-F]{12}")) throw new IllegalArgumentException("MAC inválido.");
        byte[] hardware = new byte[6];
        boolean nonzero = false;
        for (int i = 0; i < hardware.length; i++) {
            hardware[i] = (byte) Integer.parseInt(mac.substring(i * 2, i * 2 + 2), 16);
            nonzero |= hardware[i] != 0;
        }
        if (!nonzero || (hardware[0] & 1) != 0) throw new IllegalArgumentException("MAC inválido.");
        byte[] bytes = new byte[102];
        java.util.Arrays.fill(bytes, 0, 6, (byte) 255);
        for (int i = 6; i < bytes.length; i += 6) System.arraycopy(hardware, 0, bytes, i, 6);
        return bytes;
    }

    private static int ipv4(String address) {
        if (address == null || !address.matches("(?:[0-9]{1,3}\\.){3}[0-9]{1,3}"))
            throw new IllegalArgumentException("IP inválido.");
        int value = 0;
        for (String piece : address.split("\\.")) {
            int octet = Integer.parseInt(piece);
            if (octet > 255) throw new IllegalArgumentException("IP inválido.");
            value = (value << 8) | octet;
        }
        return value;
    }

    private static boolean privateAddress(int value) {
        return (value >>> 24) == 10 || (value >>> 20) == 0xac1 || (value >>> 16) == 0xc0a8;
    }

    private static int mask(int prefix) { return -1 << (32 - prefix); }
}
