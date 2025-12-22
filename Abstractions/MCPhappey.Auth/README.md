# MCPhappey.Auth

Implements OAuth2/OpenID Connect server endpoints for authentication, integrating with Azure AD and supporting PKCE.

## Architecture

```mermaid
flowchart TD
    subgraph Controllers
        AuthorizationController
        AuthorizationServerMetadataController
        CallbackController
        JwksController
        RegisterController
        TokenController
    end
    subgraph Cache
        CodeCache
        JwksCache
        PkceCache
    end
    subgraph Extensions
        AspNetCoreExtensions
        AspNetCoreWebAppExtensions
        AuthEndpoints
        HttpExtensions
        ServerExtensions
    end
    Models
    JwtValidator

    Controllers --> Cache
    Controllers --> Models
    Controllers --> Extensions
    JwtValidator --> Controllers
```

## Key Features
- OAuth2/OpenID Connect endpoints
- Azure AD integration
- PKCE and secure state handling

## Usage

Integrate as a library in your MCP server or Web API host. Provides endpoints for authentication and token management.

## Dependencies
- Microsoft.IdentityModel.Tokens
- System.IdentityModel.Tokens.Jwt
- MCPhappey.Common
