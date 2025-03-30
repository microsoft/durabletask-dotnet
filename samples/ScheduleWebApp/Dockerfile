#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the entire solution
COPY . .

# Restore and build
WORKDIR "/src/samples/ScheduleWebApp"
RUN dotnet restore "ScheduleWebApp.csproj"
RUN dotnet build "ScheduleWebApp.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/samples/ScheduleWebApp"
RUN dotnet publish "ScheduleWebApp.csproj" -c Release -o /app/publish /p:UseAppHost=false
RUN ls -la /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN ls -la
ENV ASPNETCORE_ENVIRONMENT=Production
# Connection string should be provided at runtime
# Example for running locally:
# docker run -p 8080:8080 -e DURABLE_TASK_SCHEDULER_CONNECTION_STRING="your_connection_string" schedulewebapp
ENTRYPOINT ["dotnet", "Samples.ScheduleWebApp.dll"]