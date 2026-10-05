# PetSitter

## Configure MongoDB Atlas

The application uses MongoDB.Driver. Create an Atlas cluster and database user, then allow the application's IP address under **Network Access**. Copy the cluster's connection string and provide it through .NET User Secrets for local development:

```bash
dotnet user-secrets init --project PetSitter/PetSitter.csproj
dotnet user-secrets set "ConnectionStrings:MongoDb" "mongodb+srv://<user>:<password>@<cluster-host>/?retryWrites=true&w=majority&appName=PetSitter" --project PetSitter/PetSitter.csproj
dotnet user-secrets set "MongoDb:Database" "PetSitterDb" --project PetSitter/PetSitter.csproj
dotnet run --project PetSitter/PetSitter.csproj
```

Encode special characters in the username or password for use in a URI. Do not commit Atlas credentials. For deployed environments, configure `ConnectionStrings__MongoDb` and `MongoDb__Database` as environment variables instead.

The application creates its unique indexes at startup. MongoDB IDs and references are ObjectId values represented as strings in the API and models; related documents are stored in separate collections and navigation properties are not automatically loaded. Existing SQL Server data is not migrated automatically.
