FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ .
RUN dotnet publish Emby.Plugin.LeavingSoon/Emby.Plugin.LeavingSoon.csproj -c Release -o /out

FROM scratch AS export
COPY --from=build /out/Emby.Plugin.LeavingSoon.dll /
