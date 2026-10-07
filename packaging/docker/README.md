# Docker and Unraid

Build the Debian package and then the Docker image:

```sh
bash packaging/debian/build-deb.sh
bash packaging/docker/build-docker.sh
```

The image starts the Avalonia desktop in TigerVNC and exposes a noVNC browser
interface on port `6081` and native VNC on `5902`. Set `VNC_PASSWORD` to a
6-8-character value. Leaving it blank disables VNC authentication and is only
appropriate on a trusted network. Bind exposed ports to localhost or firewall
them when they are not protected by a trusted LAN.

For Unraid, import `unraid-template.xml`, set the image to your registry image,
and map the host folder you intend to rename to `/media` with read/write access.
Map appdata to `/config`. The template runs as UID `99` / GID `100` (`nobody`
and `users`); ensure both host directories are writable by those IDs. Open the
container WebUI to use the app in a browser.
