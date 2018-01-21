#define __DEBUG

using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;

// This is an atempt to recreate UE4s class wizard, that can be called from the command line.
namespace UnrealClassWizard
{
	public class Program
	{
		const int MIN_ARG_COUNT = 2;
		const string COPYRIGHT = "(c) 2018 Jonas Reich, All rights reserved.";
		static readonly string[] DEFAULT_INCLUDES = { "CoreMinimal.h", "AnotherSharedHeader.h" };
		static void PrintHelp()
		{
			Console.WriteLine("UnrealClassWizard.exe <target-directory> <new-type-name> [<parent-type>]");
		}

		static int Main(string[] args)
		{
			if (args.Length < MIN_ARG_COUNT + 1)
			{
				Console.Error.WriteLine("UnrealClassWizard requires at least " + MIN_ARG_COUNT + " parameters.");
				PrintHelp();
				return 1;
			}
			var targetDir = new System.IO.DirectoryInfo(System.IO.Path.GetFullPath(args[1]));
			string newTypeName = args[2];
			string parentTypeName = args.Length > 3 ? args[3] : "PARENT_TYPE";

			// TODO: privatePublicDir is never initialized!
			if (!TraversePath(targetDir, out string projectName, out var projectDir, out var sourceDir, out var moduleDir, out bool separatePrivatePublic)) return 1;

			// Remove single letter prefix (A, U, F) from newClassName
			string fileName = newTypeName.Substring(1);

			if (!DeterminePaths(targetDir, separatePrivatePublic, fileName, moduleDir, out string headerPath, out string sourcePath)) return 1;

			WriteHeaderContents(System.IO.File.CreateText(headerPath), newTypeName, ref parentTypeName, moduleDir, fileName);
			WriteSourceContents(moduleDir, fileName, System.IO.File.CreateText(sourcePath));

			return 0;
		}

		static bool IsProjectRoot(System.IO.DirectoryInfo dir)
		{
			return System.IO.Directory.GetFiles(dir.FullName, "*.uproject").Length > 0;
		}

		static bool TraversePath(System.IO.DirectoryInfo targetDir, out string projectName, out System.IO.DirectoryInfo projectDir, out System.IO.DirectoryInfo sourceDir, out System.IO.DirectoryInfo moduleDir, out bool separatePrivatePublic)
		{
			projectName = "";
			projectDir = sourceDir = moduleDir = null;
			separatePrivatePublic = false;

			if (!targetDir.Exists)
			{
				Console.Error.WriteLine("Target directory not found.");
				return false;
			};

			if (IsProjectRoot(targetDir))
			{
				Console.Error.WriteLine(".uproject file detected in target directory. Please create source files in '<project-root>/Source/ModuleName/**/*'.");
				return false;
			}

			System.IO.DirectoryInfo currentDir = targetDir, lastDir = null, secondToLastDir = null, thirdToLastDir = null;
			while (currentDir.Parent != null)
			{
				thirdToLastDir = secondToLastDir;
				secondToLastDir = lastDir;
				lastDir = currentDir;
				currentDir = currentDir.Parent;

				if (IsProjectRoot(currentDir))
				{
					projectName = System.IO.Path.GetFileNameWithoutExtension(System.IO.Directory.GetFiles(currentDir.FullName, "*.uproject")[0]);
					projectDir = currentDir;

					if (lastDir != null && lastDir.Name == "Source")
					{
						sourceDir = lastDir;
					}
					else
					{
						Console.Error.WriteLine("Project source directory not found.");
						Console.Error.WriteLine("Expected source directory '<project-root>/Source/', found '" + lastDir.FullName + "'.");
						return false;
					}

					if (secondToLastDir != null)
					{
						moduleDir = secondToLastDir;
					}
					else
					{
						Console.Error.WriteLine("No module directory found. Please specify a target path that is part of a module.");
						return false;
					}

					if (thirdToLastDir != null && (thirdToLastDir.Name == "Public" || thirdToLastDir.Name == "Private"))
					{
						separatePrivatePublic = true;
					}
				}
			}

			if (projectDir == null)
			{
				Console.Error.WriteLine("Project root not found.");
				return false;
			}

			return true;
		}

		static bool DeterminePaths(System.IO.DirectoryInfo targetDir, bool separatePrivatePublic, string fileName, System.IO.DirectoryInfo moduleDir, out string headerPath, out string sourcePath)
		{
			headerPath = sourcePath = "";

			if (separatePrivatePublic)
			{
				string trail = targetDir.FullName.Replace(moduleDir.FullName, "");
				if (trail.StartsWith("\\Public"))
				{
					trail = trail.Substring(7);
				}
				else if (trail.StartsWith("\\Private"))
				{
					trail = trail.Substring(8);
				}
				else
				{
					Console.WriteLine("Couldn't detect if private or public dir: " + trail);
					return false;
				}

				headerPath = moduleDir.FullName + "\\Public" + trail + "\\" + fileName + ".h";
				sourcePath = moduleDir.FullName + "\\Private" + trail + "\\" + fileName + ".cpp";
			}
			else
			{
				headerPath = targetDir.FullName + fileName + ".h";
				sourcePath = targetDir.FullName + fileName + ".cpp";
			}

			if (String.IsNullOrEmpty(headerPath))
			{
				Console.Error.WriteLine("Couldn't determine header file path");
				return false;
			}
			if (String.IsNullOrEmpty(sourcePath))
			{
				Console.Error.WriteLine("Couldn't determine source file path");
				return false;
			}

			if ((new System.IO.FileInfo(headerPath)).Exists)
			{
				Console.Error.WriteLine("Header file '" + headerPath + "' already exists.");
				return false;
			}
			else if ((new System.IO.FileInfo(sourcePath)).Exists)
			{
				Console.Error.WriteLine("Source file '" + sourcePath + "' already exists.");
				return false;
			}

			return true;
		}


		private static void WriteSourceContents(System.IO.DirectoryInfo moduleDir, string fileName, System.IO.StreamWriter targetSourceFile)
		{
			targetSourceFile.WriteLine("// " + COPYRIGHT);
			targetSourceFile.WriteLine();
			targetSourceFile.WriteLine("#include \"" + moduleDir.Name + ".h\"");
			targetSourceFile.WriteLine("#include \"" + fileName + ".h\"");
			targetSourceFile.WriteLine();
			targetSourceFile.Flush();
		}

		private static void WriteHeaderContents(System.IO.StreamWriter targetHeaderFile, string newTypeName, ref string parentTypeName, System.IO.DirectoryInfo moduleDir, string fileName)
		{
			targetHeaderFile.WriteLine("// " + COPYRIGHT);
			targetHeaderFile.WriteLine();
			targetHeaderFile.WriteLine("#pragma once");
			targetHeaderFile.WriteLine("#include \"CoreMinimal.h\"");

			foreach (var include in DEFAULT_INCLUDES)
				targetHeaderFile.WriteLine("#include \"" + include + "\"");

			targetHeaderFile.WriteLine("#include \"" + fileName + ".generated.h\"");
			targetHeaderFile.WriteLine();

			if (IsNewtypeUnrealClass(newTypeName, ref parentTypeName))
			{
				targetHeaderFile.WriteLine("/**");
				targetHeaderFile.WriteLine(" * ");
				targetHeaderFile.WriteLine(" */");
				targetHeaderFile.WriteLine("UCLASS()");
				targetHeaderFile.WriteLine("class " + moduleDir.Name.ToUpper() + "_API " + newTypeName + " : public " + parentTypeName);
				targetHeaderFile.WriteLine("{");
				targetHeaderFile.WriteLine("\tGENERATED_BODY()");
				targetHeaderFile.WriteLine("public:");
				targetHeaderFile.WriteLine();
				targetHeaderFile.WriteLine("};");
			}
			else if (newTypeName.StartsWith("E"))
			{
				// Enum
				if (!String.IsNullOrEmpty(parentTypeName))
					Console.Error.WriteLine("Enum types don't support inheritance. Ignoring <parent-type> parameter.");

				targetHeaderFile.WriteLine("/**");
				targetHeaderFile.WriteLine(" * ");
				targetHeaderFile.WriteLine(" */");
				targetHeaderFile.WriteLine("UENUM()");
				targetHeaderFile.WriteLine("enum class " + newTypeName + " : uint8 ");
				targetHeaderFile.WriteLine("{");
				targetHeaderFile.WriteLine();
				targetHeaderFile.WriteLine("};");
			}
			else if (newTypeName.StartsWith("F"))
			{
				// Struct
				targetHeaderFile.WriteLine("/**");
				targetHeaderFile.WriteLine(" * ");
				targetHeaderFile.WriteLine(" */");
				targetHeaderFile.WriteLine("USTRUCT()");
				targetHeaderFile.WriteLine("struct " + newTypeName + (String.IsNullOrEmpty(parentTypeName) ? "" : (parentTypeName + " : ")));
				targetHeaderFile.WriteLine("{");
				targetHeaderFile.WriteLine("\tGENERATED_BODY()");
				targetHeaderFile.WriteLine();
				targetHeaderFile.WriteLine("};");
			}
			targetHeaderFile.Flush();
		}

		private static bool IsNewtypeUnrealClass(string newTypeName, ref string parentTypeName)
		{
			bool newTypeIsClass = true;
			if (newTypeName.StartsWith("U"))
			{
				if (String.IsNullOrEmpty(parentTypeName))
					parentTypeName = "UObject";
			}
			else if (newTypeName.StartsWith("A"))
			{
				if (String.IsNullOrEmpty(parentTypeName))
					parentTypeName = "AActor";
			}
			else
			{
				newTypeIsClass = false;
			}

			return newTypeIsClass;
		}
	}
}
