---
description: "StatsHub development guidance"
applyTo: "**"
---

# StatsHub Project Guidelines

Welcome to StatsHub! This document provides guidance for developing this full-stack statistics dashboard.

## Getting Started

### First Time Setup

1. **Install .NET SDK**: Ensure you have .NET 10.0 or higher installed
2. **Install Node.js**: Use Node.js 18+ for the frontend
3. **Clone and Setup**:
   ```bash
   cd StatsHub
   # Backend
   cd backend/StatsHub.Api && dotnet restore
   # Frontend
   cd ../../frontend && npm install
   ```

### Running the Application

**Terminal 1 - Backend**:
```bash
cd backend/StatsHub.Api
dotnet run
# API runs on http://localhost:5132
```

**Terminal 2 - Frontend**:
```bash
cd frontend
npm run dev
# Frontend runs on http://localhost:5173
```

Visit `http://localhost:5173` in your browser.

## Project Architecture

### Backend Structure
- **Controllers/**: HTTP request handlers (RESTful API endpoints)
- **Models/**: Data transfer objects (DTOs) and domain models
- **Services/**: Business logic and data access layer
- **Program.cs**: Dependency injection and middleware configuration

### Frontend Structure
- **src/main.tsx**: Application entry point
- **src/App.tsx**: Root React component
- **src/**: Reusable components and utilities
- **vite.config.ts**: Vite build configuration and API proxy

## Development Standards

### Code Style
- **Backend**: Follow Microsoft's C# coding conventions
- **Frontend**: Use ESLint configuration defined in `.eslintrc.cjs`
- **Both**: Use meaningful variable names and comments

### API Design
- Use RESTful principles for endpoints
- Return consistent JSON response structures
- Handle errors gracefully with appropriate HTTP status codes
- Document all endpoints in comments

### React Components
- Keep components focused and single-responsibility
- Use TypeScript for all components
- Extract reusable logic into custom hooks
- Prefer functional components over class components

## Testing & Quality

- Run `npm run lint` in frontend to check code quality
- Use `dotnet build` in backend to compile and check errors
- Write meaningful error messages for debugging
- Test API integration thoroughly before merging

## Common Tasks

### Adding a New API Endpoint
1. Create controller in `backend/StatsHub.Api/Controllers/`
2. Add corresponding model/DTO in `Models/`
3. Implement business logic in `Services/`
4. Call from React component with `fetch('/api/endpoint')`

### Creating a New React Component
1. Create file in `frontend/src/`
2. Use React hooks for state management
3. Add TypeScript types for props
4. Import and use in App.tsx or other components

### Debugging
- Backend: Check console output when running `dotnet run`
- Frontend: Open DevTools (F12) to check console for errors
- Network: Check browser Network tab to verify API calls
- Use VS Code debugger for breakpoints

## Environment Configuration

Copy example env files and customize:
```bash
cp .env.example .env
cp backend/.env.example backend/.env
cp frontend/.env.example frontend/.env
```

## Git Workflow

- Create feature branches for new work: `git checkout -b feature/your-feature`
- Commit with descriptive messages
- Push and create pull requests for review
- Keep main branch stable and deployable

## Resources

- [.NET 10 Documentation](https://docs.microsoft.com/dotnet/)
- [React Documentation](https://react.dev)
- [TypeScript Handbook](https://www.typescriptlang.org/docs/)
- [Vite Guide](https://vitejs.dev/guide/)

## Getting Help

- Use the StatsHub Developer agent (`statshub-dev.agent.md`) for full-stack issues
- Check the README.md for project overview
- Review existing code patterns before implementing new features

