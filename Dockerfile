FROM mcr.microsoft.com/dotnet/sdk:8.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

COPY NuGet.Config ./
COPY VideoAudioProcessor.sln ./
COPY VideoAudioProcessor/VideoAudioProcessor.csproj VideoAudioProcessor/
COPY VideoAudioProcessor.IntegrationTests/VideoAudioProcessor.IntegrationTests.csproj VideoAudioProcessor.IntegrationTests/

RUN dotnet restore "VideoAudioProcessor.IntegrationTests/VideoAudioProcessor.IntegrationTests.csproj" --configfile NuGet.Config

COPY . .

CMD ["dotnet", "test", "VideoAudioProcessor.IntegrationTests/VideoAudioProcessor.IntegrationTests.csproj", "--no-restore", "-c", "Release", "-v", "minimal"]
