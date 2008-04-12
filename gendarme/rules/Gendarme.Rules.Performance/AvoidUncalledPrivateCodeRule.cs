//
// Gendarme.Rules.Performance.AvoidUncalledPrivateCodeRule
//
// Authors:
//	Nidhi Rawal <sonu2404@gmail.com>
//	Sebastien Pouliot  <sebastien@ximian.com>
//
// Copyright (c) <2007> Nidhi Rawal
// Copyright (C) 2007-2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Gendarme.Framework;
using Gendarme.Framework.Rocks;

namespace Gendarme.Rules.Performance {
	
	[Problem ("This private or internal (assembly-level) member does not have callers in the assembly, is not invoked by the common language runtime, and is not invoked by a delegate.")]
	[Solution ("Remove the non-callable code or add the code that calls it.")]
	public class AvoidUncalledPrivateCodeRule: Rule,IMethodRule {

		// we should move these methods into an helper class (e.g. MethodHelper)
		// and reuse them in other rules

		private static bool IsSerializationConstructor (MethodDefinition method)
		{
			// it must be an instance constructor with 2 parameters
			if (!method.IsConstructor || method.IsStatic || (method.Parameters.Count != 2))
				return false;

			if (method.Parameters [0].ParameterType.FullName != "System.Runtime.Serialization.SerializationInfo")
				return false;

			return (method.Parameters [1].ParameterType.FullName == "System.Runtime.Serialization.StreamingContext");
		}
		
		private static bool IsExplicitImplementationOfInterface (MethodDefinition method)
		{
			// quick out if the name doesn't include a .
			if (!method.Name.Contains ("."))
				return false;

			TypeDefinition type = (method.DeclaringType as TypeDefinition);
			foreach (TypeReference intf in type.Interfaces) {
				if (method.Name.StartsWith (intf.FullName))
					return true;
			}
			return false;
		}
		
		//

		public RuleResult CheckMethod (MethodDefinition method)
		{
			// #1 - rule doesn't apply to static ctor
			// #2 - rule doesn't apply if the method is the assembly entry point
			// #3 - rule doesn't apply to Main
			// #4 - rule doesn't apply if the method is generated by the compiler or by a tool
			if ((method.IsStatic && method.IsConstructor) || method.IsEntryPoint () || method.IsMain () || method.IsGeneratedCode ())
				return RuleResult.DoesNotApply;

			// ok, the rule applies

			// check if the method is private 
			if (method.IsPrivate) {
				// it's ok for have unused private ctor (and common before static class were introduced in 2.0)
				// this also covers private serialization constructors
				if (method.IsConstructor)
					return RuleResult.Success;

				// it's ok (used or not) if it's required to implement explicitely an interface
				if (IsExplicitImplementationOfInterface (method))
					return RuleResult.Success;

				// then we must check if this type use the private method
				bool called = CheckTypeForMethodUsage ((method.DeclaringType as TypeDefinition), method);

				// then we must check if this type's nested types (if any) use the private method
				if (!called) {
					foreach (TypeDefinition nested in (method.DeclaringType as TypeDefinition).NestedTypes) {
						called = CheckTypeForMethodUsage (nested, method);
						if (called)
							break;
					}
				}

				// report if the private method is uncalled
				if (!called) {
					Runner.Report (method, Severity.High, Confidence.Normal, "The private method code is not used in it's declaring type");
					return RuleResult.Failure;
				}
			}

			// check if method is internal
			if (method.IsAssembly) {
				// internal ctor for serialization are ok
				if (IsSerializationConstructor (method))
					return RuleResult.Success;

				// then we must check if something in the assembly is using this method
				if (!CheckAssemblyForMethodUsage (method.DeclaringType.Module.Assembly, method)) {
					Runner.Report (method, Severity.High, Confidence.Normal, "The internal method code is not used in it's declaring assembly");
					return RuleResult.Failure;
				}
			}

			// then method is accessible
			return RuleResult.Success;
		}

		private static bool CheckAssemblyForMethodUsage (AssemblyDefinition ad, MethodDefinition md)
		{
			// scan each module
			foreach (ModuleDefinition module in ad.Modules) {
				// scan each type
				foreach (TypeDefinition type in module.Types) {
					if (CheckTypeForMethodUsage (type, md))
						return true;
				}
			}
			return false;
		}

		private static bool CheckTypeForMethodUsage (TypeDefinition td, MethodDefinition md)
		{
			// check every constructor for the type
			foreach (MethodDefinition ctor in td.Constructors) {
				// skip ourself
				if (ctor == md)
					continue;
				if (CheckMethodUsage (ctor, md))
					return true;
			}
			// check every method for the type
			foreach (MethodDefinition method in td.Methods) {
				// skip check ourself (even with recursion if no one call us then it's still unused)
				if (method == md)
					continue;
				if (CheckMethodUsage (method, md))
					return true;
			}
			return false;
		}

		private static bool CheckMethodUsage (MethodDefinition method, MethodDefinition md)
		{
			if (!method.HasBody)
				return false;

			foreach (Instruction instruction in method.Body.Instructions) {
				if (instruction.Operand == md)
					return true;
				if (instruction.OpCode.Code == Code.Callvirt) {
					foreach (MethodReference virtmd in md.Overrides) {
						if (instruction.Operand == virtmd)
							return true;
					}
				}
			}
			return false;
		}
	}
}
