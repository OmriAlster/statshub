# StatsHub Development Setup

This directory contains configuration and customizations for StatsHub development.

## Structure

```
.github/
├── agents/              # Custom agent definitions
│   └── statshub-dev.agent.md    # StatsHub Developer agent
├── copilot-instructions.md      # Project-wide guidelines
└── README.md           # This file
```

## Custom Agent: StatsHub Developer

Located in `agents/statshub-dev.agent.md`

### When to Use
- Developing new features in backend or frontend
- Debugging full-stack issues
- Refactoring code across services
- Building API integration

### Features
- Understands both ASP.NET Core and React architecture
- Optimized for TypeScript and C# development
- Includes tool filtering for efficient development
- Provides workflow guidance for common tasks

### Invoke in Chat
Simply mention StatsHub development context, and the agent will be suggested automatically based on the files you're working with.

## Project Configuration Files

See `/frontend/.eslintrc.cjs` and `/backend/StatsHub.Api/` for tool-specific configurations.

