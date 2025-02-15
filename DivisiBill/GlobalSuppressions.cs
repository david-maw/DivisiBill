// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Usage", "CsWinRT1028:Class should be marked partial", Justification = "WinRT interface and AOT related but DivisiBill doesn't use AOT on WinRT")]