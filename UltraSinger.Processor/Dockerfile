# UltraSinger.Processor with its whole Python toolchain (UltraSinger, demucs, syncedlyrics,
# yt-dlp) in one image, so the processor and the tools it shells out to see identical paths.
# Build from the repo root:  docker compose build   (see compose.yaml)

# ---- .NET build -------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS dotnet-build
WORKDIR /src
COPY global.json ./
COPY UltraSinger.Contracts/UltraSinger.Contracts.csproj UltraSinger.Contracts/
COPY UltraSinger.Processor/UltraSinger.Processor.csproj UltraSinger.Processor/
RUN dotnet restore UltraSinger.Processor/UltraSinger.Processor.csproj
COPY UltraSinger.Contracts/ UltraSinger.Contracts/
COPY UltraSinger.Processor/ UltraSinger.Processor/
RUN dotnet publish UltraSinger.Processor/UltraSinger.Processor.csproj -c Release -o /out --no-restore

# ---- Python venv ------------------------------------------------------------------------
# Same base as the runtime image so anything compiled from an sdist links against the same libc.
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS python-build
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential \
    && rm -rf /var/lib/apt/lists/*
COPY --from=ghcr.io/astral-sh/uv:0.13.9 /uv /usr/local/bin/uv
ENV UV_PYTHON_INSTALL_DIR=/opt/uv-python \
    UV_PROJECT_ENVIRONMENT=/opt/venv \
    UV_LINK_MODE=copy \
    UV_COMPILE_BYTECODE=1
WORKDIR /build/python
COPY UltraSinger.Processor/python/pyproject.toml UltraSinger.Processor/python/uv.lock ./
COPY UltraSinger.Processor/UltraSinger/pyproject.toml ../UltraSinger/pyproject.toml
# UltraSinger is run by path from /app/UltraSinger, so only its dependencies are installed.
# That also keeps this (multi-GB) layer cached across submodule source changes.
RUN uv sync --frozen --no-dev --no-install-package ultrasinger

# ---- Runtime ----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:11.0

# Match the host user so bind-mounted library/data/cookie directories are readable and writable.
ARG UID=1000
ARG GID=1000
RUN groupmod -g ${GID} app && usermod -u ${UID} -g ${GID} app \
    && apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=python-build --chown=app:app /opt/uv-python /opt/uv-python
# Writable by the app user: pyhyphen downloads its dictionaries into its own package directory.
COPY --from=python-build --chown=app:app /opt/venv /opt/venv
COPY --chown=app:app UltraSinger.Processor/UltraSinger /app/UltraSinger
COPY --from=dotnet-build --chown=app:app /out /app

# Model downloads (whisper, demucs, ...) land in /cache; mount a volume so they persist.
ENV PATH=/opt/venv/bin:$PATH \
    HOME=/home/app \
    XDG_CACHE_HOME=/cache \
    HF_HOME=/cache/huggingface \
    TORCH_HOME=/cache/torch \
    NVIDIA_VISIBLE_DEVICES=all \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility \
    ASPNETCORE_HTTP_PORTS=5209 \
    ProcessorOptions__PythonEnvironmentPath=/opt/venv \
    ProcessorOptions__UltraSingerPath=/app/UltraSinger \
    ProcessorOptions__UltraStarDeluxeLocalLibraryPath=/library/songs \
    ProcessorOptions__DatabasePath=/data

RUN mkdir -p /cache /data /library/songs && chown app:app /cache /data /library /library/songs
USER app
WORKDIR /app
EXPOSE 5209
ENTRYPOINT ["dotnet", "UltraSinger.Processor.dll"]
