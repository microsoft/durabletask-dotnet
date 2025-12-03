# GitHub Copilot Instructions

This repository contains C# code.

The purpose of this code is to provide a standalone SDK that can be used to interact with the Durable Task coding paradigm, both through Azure Functions (Durable Functions) and non-function based Durable Task Scheduler (DTS).

When contributing to this repository, please follow these guidelines:

## C# Code Guidelines

Here are some general guidelines that apply to all code.

- The top of all *.cs files should have a copyright notice: 
  ```csharp
  // Copyright (c) Microsoft Corporation.
  // Licensed under the MIT License.
  ```
- All public methods and classes should have XML documentation comments.
- No change should introduce a breaking change unless an exception is otherwise noted in the PR Summary, linked github issue, or discussion.
  - Breaking change reference: https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/breaking-change-rules.md 
- Use `this.` for accessing class members.
- Use the Async suffix on the name of all async methods.
- Ensure that all private classes, that do not serve as base classes, are sealed.

### C# Sample Code Guidelines

Sample code is located in the `samples` directory.

When adding a new sample, follow these steps:

- The sample should be a standalone .NET project in one of the subdirectories of the samples directory.
- The directory name should be the same as the project name.
- The directory should contain a README.md file that explains what the sample does and how to run it.
- The README.md file should follow the same format as other samples.
- The csproj file should match the directory name.
- The csproj file should be configured in the same way as other samples.
- The project should preferably contain a single Program.cs file that contains all the sample code.
- The sample should be added to the solution file in the samples directory.
- The sample should be tested to ensure it works as expected.
- A reference to the new sample should be added to the README.md file in the parent directory of the new sample.

The sample code should follow these guidelines:

- Configuration settings should be read from environment variables, e.g. `var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");`.
- Environment variables should use upper snake_case naming convention.
- Secrets should not be hardcoded in the code or committed to the repository.
- The code should be well-documented with comments explaining the purpose of each step.
- The code should be simple and to the point, avoiding unnecessary complexity.
- Prefer inline literals over constants for values that are not reused. For example, use `new ChatClientAgent(chatClient, instructions: "You are a helpful assistant.")` instead of defining a constant for "instructions".
- Prefer defining variables using types rather than var, to help users understand the types involved.
- Follow the patterns in the samples in the same directories where new samples are being added.
- The structure of the sample should be as follows:
  - Add a comment describing what the sample is demonstrating.
  - Then add the necessary using statements.
  - Then add the main code logic.
  - Finally, add any helper methods or classes at the bottom of the file.

### C# Unit Test Guidelines

Unit tests are located in the `test` root directory in projects with a `.Tests.csproj` suffix.

Unit tests should follow these guidelines:

- Add Arrange, Act and Assert comments for each 
- Use the Moq library for mocking objects where possible.
- Validate that each test actually tests the target behavior, e.g. we should not have tests that create a mock, call the mock and then verify that the mock was called, without the target code being involved. We also shouldn't have tests that test language features, e.g. something that the compiler would catch anyway.
- Avoid adding excessive comments to tests. Instead favor clear easy to understand code.
- Follow the patterns in the unit tests in the same project or classes to which new tests are being added.

## Code Review Guidelines

When reviewing code, follow these guidelines:

- Provide all review comments in a single review pass. Avoid scattering feedback across multiple partial reviews; consolidate findings into one coherent review round.
- Do not generate false-positive or already-resolved comments when new commits are pushed. Only surface issues that still apply after the latest changes, and avoid re-posting comments that have been addressed or are no longer relevant.