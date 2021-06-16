FROM mcr.microsoft.com/dotnet/core/aspnet:3.1.16
ARG source
WORKDIR /app
EXPOSE 80
EXPOSE 5000
COPY ${source:-obj/Docker/publish} .
ENTRYPOINT ["dotnet", "MyVendor.ServiceBroker.dll"]
