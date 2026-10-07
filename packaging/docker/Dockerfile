FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive

COPY bulk-rename-utility.deb /tmp/bulk-rename-utility.deb

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates novnc openbox tigervnc-standalone-server \
        tigervnc-tools websockify x11-xserver-utils x11-utils \
        /tmp/bulk-rename-utility.deb \
    && rm -f /tmp/bulk-rename-utility.deb \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --shell /usr/sbin/nologin app \
    && mkdir -p /config /media /tmp/.X11-unix \
    && chown app:app /config /media \
    && chmod 1777 /tmp/.X11-unix

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod 0755 /usr/local/bin/docker-entrypoint.sh

ENV HOME=/config \
    VNC_GEOMETRY=1440x900 \
    VNC_PORT=5902 \
    NOVNC_PORT=6081 \
    BULKRENAME_DEFAULT_PATH=/media

EXPOSE 5902/tcp 6081/tcp
USER 1000:1000
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
