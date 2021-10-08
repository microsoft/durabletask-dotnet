#FROM mcr.microsoft.com/dotnet/sdk:5.0 as build
#
#WORKDIR /src
#COPY api/api.csproj .
#RUN dotnet restore
#
#COPY api/ .
#RUN dotnet publish -c Release -o /out
#
#FROM mcr.microsoft.com/dotnet/aspnet:5.0 
#
#WORKDIR /app
#COPY --from=build /out/ /app
#
#ENTRYPOINT ["dotnet", "/app/api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:6.0 as build

COPY . /root
RUN cd /root/samples/WebAPI && dotnet restore
RUN cd /root/samples/WebAPI && dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:6.0 

WORKDIR /app
COPY --from=build /out/ /app

EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/WebAPI.dll"]