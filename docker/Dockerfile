# Runs the ASP.NET MVC 5 app on macOS/Linux (Docker Desktop, OrbStack ...) with Mono + the XSP web server.
# On Windows you can simply open Wavelength.sln in Visual Studio and press F5 instead.
FROM mono:6.12

# Debian buster is end-of-life: use the archive to install XSP (the ASP.NET web server for Mono).
RUN sed -i 's|http://deb.debian.org/debian-security|http://archive.debian.org/debian-security|; s|http://deb.debian.org/debian|http://archive.debian.org/debian|' /etc/apt/sources.list \
 && sed -i '/buster-updates/d' /etc/apt/sources.list \
 && apt-get -o Acquire::Check-Valid-Until=false update \
 && apt-get install -y --no-install-recommends mono-xsp4 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY packages/ packages/
COPY Wavelength.Web/ Wavelength.Web/
RUN msbuild Wavelength.Web/Wavelength.Web.csproj /nologo /v:minimal /p:Configuration=Release \
 && mkdir -p /app && cp -r Wavelength.Web/. /app/ \
 && rm -rf /app/obj /app/node_modules \
 && sed -i 's#customErrors mode="RemoteOnly"#customErrors mode="On"#' /app/Web.config

WORKDIR /app
EXPOSE 8080
# WAVELENGTH_DB (connection string) is set by docker-compose.yml
CMD ["xsp4", "--port", "8080", "--address", "0.0.0.0", "--root", "/app", "--nonstop"]
