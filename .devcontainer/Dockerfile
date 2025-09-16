# Use the amd64 version so that the linux-x64 package works on your M1/M4 Mac
ARG VARIANT=9.0
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:${VARIANT}

# Create non-root user
RUN groupadd -g 1000 vscode && \
    useradd -u 1000 -g vscode -s /bin/bash -m vscode

# Install essential development tools
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive && \
    apt-get -y install --no-install-recommends \
      curl \
      wget \
      unzip \
      ca-certificates \
      gnupg \
      lsb-release \
      procps \
      nodejs \
      npm \
      tar \
      gdal-bin

# Install global npm packages and fix permissions for auto-updates
RUN npm install -g @anthropic-ai/claude-code && \
    chown -R vscode:vscode $(npm config get prefix) || \
    chown -R vscode:vscode /usr/local/lib/node_modules /usr/local/bin

# Install AZ CLI (if needed)
RUN curl -sL https://aka.ms/InstallAzureCLIDeb | bash

# Clean up apt caches
RUN apt-get clean && rm -rf /var/lib/apt/lists/*

# Switch to non-root user
USER vscode

ENV PATH $PATH:/home/vscode/.dotnet:/home/vscode/.dotnet/tools
