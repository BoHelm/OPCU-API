# syntax=docker/dockerfile:1
# Linux amd64, unveränderliches Debian-Image. APT prüft Repository-Signaturen und Paket-Hashes.
# SDK 10.0.402 enthält Runtime/ASP.NET 10.0.13. Keine ungebundenen Master-/latest-Abhängigkeiten.
FROM debian:13.6-slim@sha256:d7e12182ce18b85b93007c1dedf31f2d29e01ccf3182cc4017c709b6259bc132 AS native
RUN test "$(dpkg --print-architecture)" = amd64 && \
    apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates libicu76 libssl3t64 libgssapi-krb5-2 zlib1g libstdc++6 && \
    rm -rf /var/lib/apt/lists/*

FROM native AS build
ENV DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
# Optionaler öffentlicher Unternehmens-CA für den Build-Proxy. TLS-Prüfung bleibt aktiviert.
RUN --mount=type=secret,id=extra_ca \
    if [ -f /run/secrets/extra_ca ]; then cp /run/secrets/extra_ca /usr/local/share/ca-certificates/build-proxy.crt && update-ca-certificates; fi && \
    curl --fail --location --show-error --retry 2 \
    https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-linux-x64.tar.gz -o /tmp/dotnet.tar.gz && \
    echo '1904a98dda2db34ba78ba3c4fb1b224455329af91c341d83547843c1839e112b04ae699ee92fa2449a8f62249196a938723078c0c60b9f65c88539d5687729c2  /tmp/dotnet.tar.gz' | sha512sum -c - && \
    mkdir -p /opt/dotnet && tar -xzf /tmp/dotnet.tar.gz -C /opt/dotnet && rm /tmp/dotnet.tar.gz
WORKDIR /source
COPY . .
RUN dotnet restore ConnectedKitchenGateway.sln --locked-mode && \
    dotnet publish src/Gateway/Gateway.csproj -c Release --no-restore -o /out/gateway && \
    dotnet publish src/DemoApi/DemoApi.csproj -c Release --no-restore -o /out/demo && \
    chmod -R a+rX /out

FROM native AS runtime
ENV DOTNET_ROOT=/opt/dotnet PATH=/opt/dotnet:$PATH
COPY --from=build /opt/dotnet/dotnet /opt/dotnet/dotnet
COPY --from=build /opt/dotnet/host /opt/dotnet/host
COPY --from=build /opt/dotnet/shared /opt/dotnet/shared
COPY --from=build /opt/dotnet/LICENSE.txt /opt/dotnet/ThirdPartyNotices.txt /opt/dotnet/
WORKDIR /app
RUN groupadd --gid 1654 app && useradd --uid 1654 --gid 1654 --create-home app && \
    mkdir -p /data /keys /demo && chown app:app /data /keys /demo
USER app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    GATEWAY_DATA_DIR=/data/gateway GATEWAY_KEY_DIR=/keys/gateway \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

FROM runtime AS demo
COPY --from=build /out/demo .
ENV ASPNETCORE_URLS=http://0.0.0.0:5090 DEMO_KEY_FILE=/demo/demo.key
EXPOSE 5090
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s CMD ["dotnet","DemoApi.dll","--health-check"]
ENTRYPOINT ["dotnet","DemoApi.dll"]

FROM runtime AS gateway
COPY --from=build /out/gateway .
EXPOSE 8080 8443 4840
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s CMD ["dotnet","Gateway.dll","--health-check"]
ENTRYPOINT ["dotnet","Gateway.dll"]
