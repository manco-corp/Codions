using Codions.Contracts.Models;

namespace Codions.BotHarness;

/// <summary>
/// Inspects a cloned repository to detect its tech stack and returns
/// the appropriate build/test/format commands and a prompt example.
/// </summary>
public static class StackDetector
{
    public static async Task<StackProfile> DetectAsync(string repoPath)
    {
        var files = await Task.Run(() =>
            Directory.GetFiles(repoPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(f => f is not null)
                .Select(f => f!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        var hasSlnOrCsproj = files.Any(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                                         || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                          || Directory.GetFiles(repoPath, "*.sln", SearchOption.AllDirectories).Length > 0
                          || Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories).Length > 0;

        var hasPackageJson = files.Contains("package.json");
        var hasAngularJson = files.Contains("angular.json");
        var hasGoMod = files.Contains("go.mod");
        var hasCargoToml = files.Contains("Cargo.toml");
        var hasPomXml = files.Contains("pom.xml");
        var hasBuildGradle = files.Contains("build.gradle") || files.Contains("build.gradle.kts");
        var hasRequirementsTxt = files.Contains("requirements.txt");
        var hasPyprojectToml = files.Contains("pyproject.toml");
        var hasSetupPy = files.Contains("setup.py");

        if (hasSlnOrCsproj)
            return DotnetProfile();

        if (hasPackageJson && hasAngularJson)
            return AngularProfile();

        if (hasPackageJson)
            return NodeProfile();

        if (hasRequirementsTxt || hasPyprojectToml || hasSetupPy)
            return PythonProfile();

        if (hasGoMod)
            return GoProfile();

        if (hasCargoToml)
            return RustProfile();

        if (hasPomXml)
            return JavaMavenProfile();

        if (hasBuildGradle)
            return JavaGradleProfile();

        return UnknownProfile();
    }

    private static StackProfile DotnetProfile() => new()
    {
        Name = "dotnet",
        FormatCommand = "dotnet format",
        BuildCommand = "dotnet build -c Release",
        TestCommand = "dotnet test",
        PromptFileExample = """
            ---FILE_EDIT: src/Services/UserService.cs---
            using System;

            namespace MyApp.Services;

            public class UserService
            {
                private readonly IUserRepository _repo;

                public UserService(IUserRepository repo)
                {
                    _repo = repo ?? throw new ArgumentNullException(nameof(repo));
                }

                public async Task<User?> GetByIdAsync(int id)
                {
                    return await _repo.FindAsync(id);
                }
            }
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile AngularProfile() => new()
    {
        Name = "angular",
        FormatCommand = "npx prettier --write .",
        BuildCommand = "npx ng build --configuration production",
        TestCommand = "npx ng test --watch=false --browsers=ChromeHeadless",
        PromptFileExample = """
            ---FILE_EDIT: src/app/app.component.ts---
            import { Component } from '@angular/core';
            import { CommonModule } from '@angular/common';
            import { RouterOutlet } from '@angular/router';

            @Component({
              selector: 'app-root',
              standalone: true,
              imports: [CommonModule, RouterOutlet],
              templateUrl: './app.component.html',
              styleUrl: './app.component.scss'
            })
            export class AppComponent {
              title: string = 'MyApp';
            }
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile NodeProfile() => new()
    {
        Name = "node",
        FormatCommand = "npx prettier --write .",
        BuildCommand = "npm run build",
        TestCommand = "npm test",
        PromptFileExample = """
            ---FILE_EDIT: src/services/userService.ts---
            import { UserRepository } from '../repositories/userRepository';
            import { User } from '../models/user';

            export class UserService {
              constructor(private readonly repo: UserRepository) {}

              async getById(id: string): Promise<User | null> {
                return this.repo.findById(id);
              }
            }
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile PythonProfile() => new()
    {
        Name = "python",
        FormatCommand = "black .",
        BuildCommand = null,
        TestCommand = "pytest",
        PromptFileExample = """
            ---FILE_EDIT: src/services/user_service.py---
            from typing import Optional
            from models.user import User
            from repositories.user_repository import UserRepository


            class UserService:
                def __init__(self, repo: UserRepository) -> None:
                    self._repo = repo

                async def get_by_id(self, user_id: int) -> Optional[User]:
                    return await self._repo.find(user_id)
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile GoProfile() => new()
    {
        Name = "go",
        FormatCommand = "gofmt -w .",
        BuildCommand = "go build ./...",
        TestCommand = "go test ./...",
        PromptFileExample = """
            ---FILE_EDIT: internal/service/user.go---
            package service

            import (
            	"context"
            	"errors"
            )

            type UserService struct {
            	repo UserRepository
            }

            func NewUserService(repo UserRepository) *UserService {
            	return &UserService{repo: repo}
            }

            func (s *UserService) GetByID(ctx context.Context, id int64) (*User, error) {
            	if id <= 0 {
            		return nil, errors.New("invalid user id")
            	}
            	return s.repo.Find(ctx, id)
            }
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile RustProfile() => new()
    {
        Name = "rust",
        FormatCommand = "cargo fmt",
        BuildCommand = "cargo build",
        TestCommand = "cargo test",
        PromptFileExample = """
            ---FILE_EDIT: src/services/user.rs---
            use crate::models::User;
            use crate::repositories::UserRepository;

            pub struct UserService {
                repo: Box<dyn UserRepository>,
            }

            impl UserService {
                pub fn new(repo: Box<dyn UserRepository>) -> Self {
                    Self { repo }
                }

                pub async fn get_by_id(&self, id: u64) -> Option<User> {
                    self.repo.find(id).await
                }
            }
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile JavaMavenProfile() => new()
    {
        Name = "java",
        FormatCommand = null,
        BuildCommand = "mvn compile",
        TestCommand = "mvn test",
        PromptFileExample = """
            ---FILE_EDIT: src/main/java/com/example/service/UserService.java---
            package com.example.service;

            import com.example.model.User;
            import com.example.repository.UserRepository;
            import java.util.Optional;

            public class UserService {
                private final UserRepository repo;

                public UserService(UserRepository repo) {
                    this.repo = repo;
                }

                public Optional<User> getById(long id) {
                    return repo.findById(id);
                }
            }
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile JavaGradleProfile() => new()
    {
        Name = "java",
        FormatCommand = null,
        BuildCommand = "gradle build",
        TestCommand = "gradle test",
        PromptFileExample = """
            ---FILE_EDIT: src/main/java/com/example/service/UserService.java---
            package com.example.service;

            import com.example.model.User;
            import com.example.repository.UserRepository;
            import java.util.Optional;

            public class UserService {
                private final UserRepository repo;

                public UserService(UserRepository repo) {
                    this.repo = repo;
                }

                public Optional<User> getById(long id) {
                    return repo.findById(id);
                }
            }
            ---END_FILE_EDIT---
            """
    };

    private static StackProfile UnknownProfile() => new()
    {
        Name = "unknown",
        FormatCommand = null,
        BuildCommand = null,
        TestCommand = null,
        PromptFileExample = """
            ---FILE_EDIT: src/example.txt---
            This is an example file.
            Replace this with the actual file content.
            ---END_FILE_EDIT---
            """
    };
}
