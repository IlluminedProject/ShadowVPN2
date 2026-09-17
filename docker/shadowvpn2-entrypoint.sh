#!/bin/sh
set -eu

FORWARD_CHAIN=SHADOWVPN2_FORWARD
NAT_CHAIN=SHADOWVPN2_NAT
VPN_INTERFACE=wg0
STATE_DIR=/data/.shadowvpn2-network
FORWARDING_STATE=$STATE_DIR/ipv4-forwarding

remove_jump() {
    table="$1"
    parent="$2"
    chain="$3"
    if [ -n "$table" ]; then
        while iptables -w 5 -t "$table" -C "$parent" -j "$chain" 2>/dev/null; do
            iptables -w 5 -t "$table" -D "$parent" -j "$chain"
        done
    else
        while iptables -w 5 -C "$parent" -j "$chain" 2>/dev/null; do
            iptables -w 5 -D "$parent" -j "$chain"
        done
    fi
}

cleanup() {
    remove_jump '' FORWARD "$FORWARD_CHAIN"
    remove_jump nat POSTROUTING "$NAT_CHAIN"

    iptables -w 5 -F "$FORWARD_CHAIN" 2>/dev/null || true
    iptables -w 5 -X "$FORWARD_CHAIN" 2>/dev/null || true
    iptables -w 5 -t nat -F "$NAT_CHAIN" 2>/dev/null || true
    iptables -w 5 -t nat -X "$NAT_CHAIN" 2>/dev/null || true

    if [ -f "$FORWARDING_STATE" ]; then
        previous=$(cat "$FORWARDING_STATE")
        sysctl -q -w "net.ipv4.ip_forward=$previous" >/dev/null
        rm -f "$FORWARDING_STATE"
    fi
}

configure() {
    mkdir -p "$STATE_DIR"

    # Remove rules left by an uncleanly terminated previous container instance.
    cleanup

    wan_interface=$(ip -4 route get 1.1.1.1 | awk '{ for (i = 1; i <= NF; i++) if ($i == "dev") { print $(i + 1); exit } }')
    if [ -z "$wan_interface" ]; then
        echo "Cannot determine WAN interface for WireGuard NAT" >&2
        exit 1
    fi

    current_forwarding=$(sysctl -n net.ipv4.ip_forward)
    printf '%s\n' "$current_forwarding" > "$FORWARDING_STATE"
    if [ "$current_forwarding" != 1 ]; then
        sysctl -q -w net.ipv4.ip_forward=1 >/dev/null
    fi

    iptables -w 5 -N "$FORWARD_CHAIN" 2>/dev/null || true
    iptables -w 5 -F "$FORWARD_CHAIN"
    iptables -w 5 -A "$FORWARD_CHAIN" -i "$VPN_INTERFACE" -j ACCEPT
    iptables -w 5 -A "$FORWARD_CHAIN" -o "$VPN_INTERFACE" -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT
    iptables -w 5 -I FORWARD -j "$FORWARD_CHAIN"

    iptables -w 5 -t nat -N "$NAT_CHAIN" 2>/dev/null || true
    iptables -w 5 -t nat -F "$NAT_CHAIN"
    iptables -w 5 -t nat -A "$NAT_CHAIN" -s 100.64.0.0/10 -o "$wan_interface" -j MASQUERADE
    iptables -w 5 -t nat -I POSTROUTING -j "$NAT_CHAIN"

    echo "Configured WireGuard forwarding and NAT via $wan_interface"
}

child=''
trap cleanup EXIT

configure

forward_term() {
    if [ -n "$child" ]; then
        kill -TERM "$child" 2>/dev/null || true
    fi
}

trap forward_term TERM INT

gosu "${APP_UID:-1654}" "$@" &
child=$!
if wait "$child"; then
    status=0
else
    status=$?
fi
child=''
exit "$status"
