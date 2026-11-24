sdk := "mcr.microsoft.com/dotnet/sdk:10.0"
jellyfin := "docker.io/jellyfin/jellyfin:12.4"
project := "Jellyfin.Plugin.TimeIsUp"
dev := "timeisup-dev"
url := "http://127.0.0.1:8096"
tz := `readlink /etc/localtime | sed "s|.*zoneinfo/||"`

# Build the plugin into artifacts/
build:
    podman run --rm -v "{{ justfile_directory() }}:/src" -v timeisup-nuget:/root/.nuget/packages \
        -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -w /src {{ sdk }} \
        dotnet publish {{ project }}/{{ project }}.csproj -c Release -o artifacts

# Run a throwaway Jellyfin with the plugin at http://localhost:8096
dev: build
    mkdir -p .dev/config/plugins/TimeIsUp .dev/cache .dev/media
    cp artifacts/{{ project }}.dll .dev/config/plugins/TimeIsUp/
    rm -f .dev/config/plugins/TimeIsUp/meta.json
    podman rm -f {{ dev }}
    podman run -d --name {{ dev }} -p 127.0.0.1:8096:8096 -e TZ={{ tz }} \
        -v ./.dev/config:/config -v ./.dev/cache:/cache -v ./.dev/media:/media:ro {{ jellyfin }}

# Generate test videos in .dev/media: three 3-minute episodes and a 20-minute movie
media:
    #!/usr/bin/env bash
    set -euo pipefail
    mkdir -p ".dev/media/Shows/Test Show/Season 01" ".dev/media/Movies/Test Movie (2026)"
    gen() {
        podman run --rm -v ./.dev/media:/media --entrypoint /usr/lib/jellyfin-ffmpeg/ffmpeg {{ jellyfin }} \
            -hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=25 -f lavfi -i sine=frequency=440 \
            -t "$1" -c:v libx264 -preset ultrafast -crf 35 -c:a aac -b:a 64k "/media/$2"
    }
    for e in 1 2 3; do gen 180 "Shows/Test Show/Season 01/Test Show S01E0$e.mkv"; done
    gen 1200 "Movies/Test Movie (2026)/Test Movie (2026).mkv"

# Finish the dev server's setup wizard: admin dev/dev, user kid without a password, test libraries
setup:
    #!/usr/bin/env bash
    set -euo pipefail
    until curl -sf {{ url }}/Startup/Configuration -o /dev/null; do sleep 1; done
    json=(-H 'Content-Type: application/json')
    curl -sf -X POST {{ url }}/Startup/Configuration "${json[@]}" \
        -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
    curl -sf {{ url }}/Startup/User -o /dev/null
    curl -sf -X POST {{ url }}/Startup/User "${json[@]}" -d '{"Name":"dev","Password":"dev"}'
    curl -sf -X POST {{ url }}/Startup/Complete
    client='MediaBrowser Client="timeisup-dev", Device="just", DeviceId="timeisup-just", Version="0.1"'
    token=$(curl -sf -X POST {{ url }}/Users/AuthenticateByName -H "Authorization: $client" "${json[@]}" \
        -d '{"Username":"dev","Pw":"dev"}' | sed -E 's/.*"AccessToken":"([^"]+)".*/\1/')
    auth=(-H "Authorization: $client, Token=\"$token\"")
    curl -sf -X POST {{ url }}/Users/New "${auth[@]}" "${json[@]}" -d '{"Name":"kid"}' -o /dev/null
    for lib in Shows:tvshows Movies:movies; do
        curl -sf -X POST "{{ url }}/Library/VirtualFolders?name=${lib%%:*}&collectionType=${lib##*:}&paths=/media/${lib%%:*}&refreshLibrary=true" \
            "${auth[@]}" "${json[@]}" -d '{"LibraryOptions":{}}'
    done

# Follow the dev server's log
logs:
    podman logs -f {{ dev }}

# Remove the dev server (keeps .dev/ so its setup survives)
down:
    podman rm -f {{ dev }}
