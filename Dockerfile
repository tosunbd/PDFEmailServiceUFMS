# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY PDFEmailServiceUFMS/PDFEmailServiceUFMS.csproj PDFEmailServiceUFMS/
RUN dotnet restore PDFEmailServiceUFMS/PDFEmailServiceUFMS.csproj

COPY . .
WORKDIR /src/PDFEmailServiceUFMS
RUN dotnet build PDFEmailServiceUFMS.csproj -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish PDFEmailServiceUFMS.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Install fonts for PDF generation
RUN apt-get update && apt-get install -y \
    libgdiplus libc6-dev fontconfig fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /app/logs /app/PDF

COPY --from=publish /app/publish .
COPY PDFEmailServiceUFMS/RDLC /app/RDLC

ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "PDFEmailServiceUFMS.dll"]
