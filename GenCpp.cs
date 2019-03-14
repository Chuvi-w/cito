// GenCpp.cs - C++ code generator
//
// Copyright (C) 2011-2019  Piotr Fusik
//
// This file is part of CiTo, see http://cito.sourceforge.net
//
// CiTo is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// CiTo is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with CiTo.  If not, see http://www.gnu.org/licenses/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Foxoft.Ci
{

public class GenCpp : GenTyped
{
	string Namespace;

	CiMethod CurrentMethod;
	readonly Dictionary<CiClass, bool> WrittenClasses = new Dictionary<CiClass, bool>();

	public GenCpp(string namespace_)
	{
		this.Namespace = namespace_;
	}

	protected override void Write(TypeCode typeCode)
	{
		switch (typeCode) {
		case TypeCode.SByte: Write("int8_t"); break;
		case TypeCode.Byte: Write("uint8_t"); break;
		case TypeCode.Int16: Write("int16_t"); break;
		case TypeCode.UInt16: Write("uint16_t"); break;
		case TypeCode.Int32: Write("int"); break;
		case TypeCode.Int64: Write("int64_t"); break;
		default: throw new NotImplementedException(typeCode.ToString());
		}
	}

	protected override void Write(CiType type, bool promote)
	{
		if (type == null) {
			Write("void");
			return;
		}

		CiIntegerType integer = type as CiIntegerType;
		if (integer != null) {
			Write(GetTypeCode(integer, promote));
			return;
		}

		if (type == CiSystem.StringPtrType) {
			Write("std::string_view");
			return;
		}
		if (type == CiSystem.StringStorageType) {
			Write("std::string");
			return;
		}

		CiArrayType array = type as CiArrayType;
		if (array != null) {
			CiArrayPtrType arrayPtr = type as CiArrayPtrType;
			if (arrayPtr != null) {
				switch (arrayPtr.Modifier) {
				case CiToken.EndOfFile:
					Write(arrayPtr.ElementType, false);
					Write(" const *");
					return;
				case CiToken.ExclamationMark:
					Write(arrayPtr.ElementType, false);
					Write(" *");
					return;
				case CiToken.Hash:
					Write("std::shared_ptr<");
					Write(arrayPtr.ElementType, false);
					Write("[]>");
					return;
				default:
					throw new NotImplementedException(arrayPtr.Modifier.ToString());
				}
			}
			CiArrayStorageType arrayStorage = (CiArrayStorageType) type;
			Write("std::array<");
			Write(arrayStorage.ElementType, false);
			Write(", ");
			Write(arrayStorage.Length);
			Write('>');
			return;
		}

		CiClassPtrType ptr = type as CiClassPtrType;
		if (ptr != null) {
			switch (ptr.Modifier) {
			case CiToken.EndOfFile:
				Write("const ");
				Write(ptr.Class.Name);
				Write(" *");
				return;
			case CiToken.ExclamationMark:
				Write(ptr.Class.Name);
				Write(" *");
				return;
			case CiToken.Hash:
				Write("std::shared_ptr<");
				Write(ptr.Class.Name);
				Write('>');
				return;
			default:
				throw new NotImplementedException(ptr.Modifier.ToString());
			}
		}

		Write(type.Name);
	}

	protected override void WriteName(CiSymbol symbol)
	{
		if (symbol is CiMember)
			WriteCamelCase(symbol.Name);
		else
			Write(symbol.Name);
	}

	public override CiExpr Visit(CiSymbolReference expr, CiPriority parent)
	{
		if (expr.Symbol is CiField)
			Write("this->");
		WriteName(expr.Symbol);
		return expr;
	}

	protected override void WriteClassStorageInit(CiClass klass)
	{
	}

	protected override void WriteArrayStorageInit(CiType type)
	{
	}

	protected override void WriteVarInit(CiNamedValue def)
	{
		if (def.Type == CiSystem.StringStorageType) {
			Write('{');
			WriteCoerced(def.Type, def.Value);
			Write('}');
		}
		else
			base.WriteVarInit(def);
	}

	protected override void WriteLiteral(object value)
	{
		if (value == null)
			Write("nullptr");
		else
			base.WriteLiteral(value);
	}

	protected override void WriteMemberOp(CiExpr left, CiSymbolReference symbol)
	{
		if (symbol.Symbol is CiConst) // FIXME
			Write("::");
		else if (left.Type is CiClassPtrType)
			Write("->");
		else
			Write('.');
	}

	void WriteStringMethod(CiExpr obj, string name, CiMethod method, CiExpr[] args)
	{
		obj.Accept(this, CiPriority.Primary);
		if (obj is CiLiteral)
			Write("sv");
		Write('.');
		Write(name);
		WriteArgsInParentheses(method, args);
	}

	void WriteArrayPtrAdd(CiExpr array, CiExpr index)
	{
		CiLiteral literal = index as CiLiteral;
		if (literal != null && (long) literal.Value == 0)
			WriteArrayPtr(array, CiPriority.Statement);
		else {
			WriteArrayPtr(array, CiPriority.Add);
			Write(" + ");
			index.Accept(this, CiPriority.Add);
		}
	}

	protected override void WriteCall(CiExpr obj, CiMethod method, CiExpr[] args, CiPriority parent)
	{
		if (IsMathReference(obj)) {
			Write("std::");
			if (method.Name == "Ceiling")
				Write("ceil");
			else
				WriteLowercase(method.Name);
			WriteArgsInParentheses(method, args);
		}
		else if (method == CiSystem.StringContains) {
			if (parent > CiPriority.Equality)
				Write('(');
			WriteStringMethod(obj, "find", method, args);
			Write(" != std::string::npos");
			if (parent > CiPriority.Equality)
				Write(')');
		}
		else if (method == CiSystem.StringIndexOf) {
			Write("static_cast<int32_t>(");
			WriteStringMethod(obj, "find", method, args);
			Write(')');
		}
		else if (method == CiSystem.StringLastIndexOf) {
			Write("static_cast<int32_t>(");
			WriteStringMethod(obj, "rfind", method, args);
			Write(')');
		}
		else if (method == CiSystem.StringStartsWith)
			WriteStringMethod(obj, "starts_with", method, args);
		else if (method == CiSystem.StringEndsWith)
			WriteStringMethod(obj, "ends_with", method, args);
		else if (method == CiSystem.StringSubstring)
			WriteStringMethod(obj, "substr", method, args);
		else if (obj.Type is CiArrayType && method.Name == "CopyTo") {
			Write("std::copy_n(");
			WriteArrayPtrAdd(obj, args[0]);
			Write(", ");
			args[3].Accept(this, CiPriority.Statement);
			Write(", ");
			WriteArrayPtrAdd(args[1], args[2]);
			Write(')');
		}
		else if (obj.Type == CiSystem.UTF8EncodingClass && method.Name == "GetString") {
			Write("std::string_view(reinterpret_cast<const char *>(");
			WriteArrayPtrAdd(args[0], args[1]);
			Write("), ");
			args[2].Accept(this, CiPriority.Statement);
			Write(')');
		}
		else {
			obj.Accept(this, CiPriority.Primary);
			if (method.CallType == CiCallType.Static)
				Write("::");
			else if (obj.Type is CiClassPtrType)
				Write("->");
			else
				Write('.');
			WriteCamelCase(method.Name);
			WriteArgsInParentheses(method, args);
		}
	}

	protected override void WriteNew(CiClass klass)
	{
		Write("std::make_shared<");
		Write(klass.Name);
		Write(">()");
	}

	protected override void WriteResource(string name, int length)
	{
		if (length >= 0) // reference as opposed to definition
			Write("CiResource::");
		foreach (char c in name)
			Write(CiLexer.IsLetterOrDigit(c) ? c : '_');
	}

	void WriteArrayPtr(CiExpr expr, CiPriority parent)
	{
		if (expr.Type is CiArrayStorageType) {
			expr.Accept(this, CiPriority.Primary);
			Write(".data()");
		}
		else {
			CiArrayPtrType arrayPtr = expr.Type as CiArrayPtrType;
			if (arrayPtr != null && arrayPtr.Modifier == CiToken.Hash) {
				expr.Accept(this, CiPriority.Primary);
				Write(".get()");
			}
			else
				expr.Accept(this, parent);
		}
	}

	protected override void WriteCoercedInternal(CiType type, CiExpr expr)
	{
		if (type is CiClassPtrType && ((CiClassPtrType) type).Modifier != CiToken.Hash) {
			if (expr.Type is CiClass) {
				Write('&');
				expr.Accept(this, CiPriority.Primary);
				return;
			}
			else {
				CiClassPtrType classPtr = expr.Type as CiClassPtrType;
				if (classPtr != null && classPtr.Modifier == CiToken.Hash) {
					expr.Accept(this, CiPriority.Primary);
					Write(".get()");
					return;
				}
			}
		}
		else if (type is CiArrayPtrType && ((CiArrayPtrType) type).Modifier != CiToken.Hash) {
			WriteArrayPtr(expr, CiPriority.Statement);
			return;
		}
		base.WriteCoercedInternal(type, expr);
	}

	protected override void WriteStringLength(CiExpr expr)
	{
		expr.Accept(this, CiPriority.Primary);
		Write(".length()");
	}

	protected override bool HasInitCode(CiNamedValue def)
	{
		return false;
	}

	void WriteConst(CiConst konst)
	{
		Write("static constexpr ");
		WriteTypeAndName(konst);
		Write(" = ");
		konst.Value.Accept(this, CiPriority.Statement);
		WriteLine(";");
	}

	public override void Visit(CiConst konst)
	{
		if (konst.Type is CiArrayType)
			WriteConst(konst);
	}

	protected override void WriteReturnValue(CiExpr expr)
	{
		WriteCoerced(this.CurrentMethod.Type, expr);
	}

	protected override void WriteCaseBody(CiStatement[] statements)
	{
		bool block = false;
		foreach (CiStatement statement in statements) {
			if (!block && statement is CiVar) {
				OpenBlock();
				block = true;
			}
			statement.Accept(this);
		}
		if (block)
			CloseBlock();
	}

	public override void Visit(CiThrow statement)
	{
		WriteLine("throw std::exception();");
		// TODO: statement.Message.Accept(this, CiPriority.Statement);
	}

	void OpenNamespace()
	{
		if (this.Namespace == null)
			return;
		WriteLine();
		Write("namespace ");
		WriteLine(this.Namespace);
		WriteLine("{");
	}

	void CloseNamespace()
	{
		if (this.Namespace != null)
			WriteLine("}");
	}

	void Write(CiEnum enu)
	{
		WriteLine();
		Write("enum class ");
		WriteLine(enu.Name);
		OpenBlock();
		bool first = true;
		foreach (CiConst konst in enu) {
			if (!first)
				WriteLine(",");
			first = false;
			WriteCamelCase(konst.Name);
			if (konst.Value != null) {
				Write(" = ");
				konst.Value.Accept(this, CiPriority.Statement);
			}
		}
		WriteLine();
		this.Indent--;
		WriteLine("};");
	}

	CiVisibility GetConstructorVisibility(CiClass klass)
	{
		switch (klass.CallType) {
		case CiCallType.Static:
			return CiVisibility.Private;
		case CiCallType.Abstract:
			return CiVisibility.Protected;
		default:
			return CiVisibility.Public;
		}
	}

	void WriteParametersAndConst(CiMethod method)
	{
		WriteParameters(method);
		if (method.CallType != CiCallType.Static && !method.IsMutator)
			Write(" const");
	}

	void WriteDeclarations(CiClass klass, CiVisibility visibility, string visibilityKeyword)
	{
		bool constructor = GetConstructorVisibility(klass) == visibility;
		IEnumerable<CiConst> consts = klass.Consts.Where(c => c.Visibility == visibility);
		IEnumerable<CiField> fields = klass.Fields.Where(f => f.Visibility == visibility);
		IEnumerable<CiMethod> methods = klass.Methods.Where(m => m.Visibility == visibility);
		if (!constructor && !consts.Any() && !fields.Any() && !methods.Any())
			return;

		Write(visibilityKeyword);
		WriteLine(":");
		this.Indent++;

		if (constructor) {
			Write(klass.Name);
			Write("()");
			if (klass.CallType == CiCallType.Static)
				Write(" = delete");
			else if (klass.Constructor == null)
				Write(" = default");
			WriteLine(";");
		}

		foreach (CiConst konst in consts)
			WriteConst(konst);

		foreach (CiField field in fields)
		{
			WriteVar(field);
			WriteLine(";");
		}

		foreach (CiMethod method in methods)
		{
			switch (method.CallType) {
			case CiCallType.Static:
				Write("static ");
				break;
			case CiCallType.Abstract:
			case CiCallType.Virtual:
				Write("virtual ");
				break;
			default:
				break;
			}
			WriteTypeAndName(method);
			WriteParametersAndConst(method);
			switch (method.CallType) {
			case CiCallType.Abstract:
				Write(" = 0");
				break;
			case CiCallType.Override:
				Write(" override");
				break;
			case CiCallType.Sealed:
				Write(" final");
				break;
			default:
				break;
			}
			WriteLine(";");
		}

		this.Indent--;
	}

	void Write(CiClass klass)
	{
		// topological sorting of class hierarchy and class storage fields
		if (klass == null)
			return;
		bool done;
		if (this.WrittenClasses.TryGetValue(klass, out done)) {
			if (done)
				return;
			throw new CiException(klass, "Circular dependency for class {0}", klass.Name);
		}
		this.WrittenClasses.Add(klass, false);
		Write(klass.Parent as CiClass);
		foreach (CiField field in klass.Fields)
			Write(field.Type.BaseType as CiClass);
		this.WrittenClasses[klass] = true;

		WriteLine();
		OpenClass(klass, klass.CallType == CiCallType.Sealed ? " final" : "", " : public ");
		this.Indent--;
		WriteDeclarations(klass, CiVisibility.Public, "public");
		WriteDeclarations(klass, CiVisibility.Protected, "protected");
		WriteDeclarations(klass, CiVisibility.Internal, "public");
		WriteDeclarations(klass, CiVisibility.Private, "private");
		WriteLine("};");
	}

	void WriteConstructor(CiClass klass)
	{
		if (klass.Constructor == null)
			return;
		Write(klass.Name);
		Write("::");
		Write(klass.Name);
		WriteLine("()");
		OpenBlock();
		Write(((CiBlock) klass.Constructor.Body).Statements);
		CloseBlock();
	}

	void WriteMethod(CiClass klass, CiMethod method)
	{
		if (method.CallType == CiCallType.Abstract)
			return;
		WriteLine();
		Write(method.Type, true);
		Write(' ');
		Write(klass.Name);
		Write("::");
		WriteCamelCase(method.Name);
		WriteParametersAndConst(method);
		this.CurrentMethod = method;
		WriteBody(method);
		this.CurrentMethod = null;
	}

	void WriteResources(Dictionary<string, byte[]> resources, bool define)
	{
		if (resources.Count == 0)
			return;
		WriteLine();
		WriteLine("namespace");
		OpenBlock();
		WriteLine("namespace CiResource");
		OpenBlock();
		foreach (string name in resources.Keys.OrderBy(k => k)) {
			if (!define)
				Write("extern ");
			Write("const std::array<uint8_t, ");
			Write(resources[name].Length);
			Write("> ");
			WriteResource(name, -1);
			if (define) {
				WriteLine(" = {");
				Write('\t');
				Write(resources[name]);
				Write(" }");
			}
			WriteLine(";");
		}
		CloseBlock();
		CloseBlock();
	}

	public override void Write(CiProgram program)
	{
		this.WrittenClasses.Clear();
		string headerFile = Path.ChangeExtension(this.OutputFile, "hpp");
		CreateFile(headerFile);
		WriteLine("#pragma once");
		WriteLine("#include <array>");
		WriteLine("#include <memory>");
		WriteLine("#include <string>");
		WriteLine("#include <string_view>");
		OpenNamespace();
		foreach (CiEnum enu in program.OfType<CiEnum>())
			Write(enu);
		foreach (CiClass klass in program.Classes) {
			Write("class ");
			Write(klass.Name);
			WriteLine(";");
		}
		foreach (CiClass klass in program.Classes)
			Write(klass);
		CloseNamespace();
		CloseFile();

		CreateFile(this.OutputFile);
		WriteLine("#include <algorithm>");
		WriteLine("#include <cmath>");
		Write("#include \"");
		Write(Path.GetFileName(headerFile));
		WriteLine("\"");
		WriteLine("using namespace std::string_view_literals;");
		WriteResources(program.Resources, false);
		OpenNamespace();
		foreach (CiClass klass in program.Classes) {
			WriteConstructor(klass);
			foreach (CiMethod method in klass.Methods)
				WriteMethod(klass, method);
		}
		WriteResources(program.Resources, true);
		CloseNamespace();
		CloseFile();
	}
}

}