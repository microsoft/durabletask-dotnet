//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

namespace DurableTask;

/// <summary>
/// Record that represents the details of an orchestration instance failure.
/// </summary>
/// <param name="Message">A summary description of the failure.</param>
/// <param name="FullText">The full details of the failure, which is often an exception call-stack.</param>
public record OrchestrationFailureDetails(string Message, string FullText);
