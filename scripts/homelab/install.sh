#!/bin/bash
# Install only. Starting the service and supplying a token are separate steps.
set -Eeuo pipefail
[[ $(id -u) == 0 ]] || { echo 'Run: sudo bash install.sh' >&2; exit 1; }
source_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
for binary in /usr/bin/python3 /usr/bin/cloudflared /usr/bin/systemctl; do
    [[ -x $binary ]] || { echo "Missing prerequisite: $binary" >&2; exit 1; }
done
for file in quick_tunnel.py project1-demo.service demo.env.example; do
    [[ -f $source_directory/$file ]] || { echo "Missing source file: $file" >&2; exit 1; }
done

install -d -o root -g root -m 0755 /opt/project1-demo
install -d -o root -g root -m 0700 /etc/project1-demo
install -o root -g root -m 0644 "$source_directory/quick_tunnel.py" /opt/project1-demo/quick_tunnel.py
install -o root -g root -m 0644 "$source_directory/project1-demo.service" /etc/systemd/system/project1-demo.service
if [[ ! -e /etc/project1-demo/config ]]; then
    install -o root -g root -m 0600 "$source_directory/demo.env.example" /etc/project1-demo/config
fi
systemctl daemon-reload
echo 'Installed. Configure the GitHub credential, then enable project1-demo.service.'
echo 'See docs/deployment/quick-tunnel-demo.md. Existing config/token were preserved.'
