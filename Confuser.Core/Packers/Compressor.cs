﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Security;
using System.Security.Permissions;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Confuser.Core
{
    public class Compressor : Packer
    {
        public override string ID
        {
            get { return "compressor"; }
        }
        public override string Name
        {
            get { return "Compressing Packer"; }
        }
        public override string Description
        {
            get { return "Reduce the size of output"; }
        }
        public override bool StandardCompatible
        {
            get { return true; ; }
        }

        protected override void PackCore(out AssemblyDefinition asm, PackerParameter parameter)
        {
            ModuleDefinition originMain = parameter.Modules[0];
            asm = AssemblyDefinition.CreateAssembly(originMain.Assembly.Name, "Pack" + originMain.Name, new ModuleParameters() { Architecture = originMain.Architecture, Kind = originMain.Kind, Runtime = originMain.Runtime });
            ModuleDefinition mod = asm.MainModule;

            Random rand = new Random();
            int key0 = rand.Next(0, 0xff);
            int key1 = rand.Next(0, 0xff);
            EmbeddedResource res = new EmbeddedResource(Encoding.UTF8.GetString(Guid.NewGuid().ToByteArray()), ManifestResourceAttributes.Private, Encrypt(parameter.PEs[0], key0));
            mod.Resources.Add(res);
            for (int i = 0; i < parameter.Modules.Length; i++)
                if (parameter.Modules[i].IsMain)
                    mod.Resources.Add(new EmbeddedResource(GetNewName(parameter.Modules[i].Assembly.Name.FullName, key1), ManifestResourceAttributes.Private, Encrypt(parameter.PEs[i], key0)));
                else
                    mod.Resources.Add(new EmbeddedResource(GetNewName(parameter.Modules[i].Name, key1), ManifestResourceAttributes.Private, Encrypt(parameter.PEs[i], key0)));  //TODO: Support for multi-module asssembly

            AssemblyDefinition ldrC = AssemblyDefinition.ReadAssembly(typeof(Iid).Assembly.Location);
            TypeDefinition t = CecilHelper.Inject(mod, ldrC.MainModule.GetType("CompressShell"));
            foreach (Instruction inst in t.GetStaticConstructor().Body.Instructions)
                if (inst.Operand is string)
                    inst.Operand = res.Name;
            foreach (Instruction inst in t.Methods.FirstOrDefault(mtd => mtd.Name == "Decrypt").Body.Instructions)
                if (inst.Operand is int && (int)inst.Operand == 0x12345678)
                    inst.Operand = key0;
            foreach (Instruction inst in t.Methods.FirstOrDefault(mtd => mtd.Name == "DecryptAsm").Body.Instructions)
                if (inst.Operand is int && (int)inst.Operand == 0x12345678)
                    inst.Operand = key1;
            t.Namespace = "";
            t.DeclaringType = null;
            t.IsNestedPrivate = false;
            t.IsNotPublic = true;
            mod.Types.Add(t);

            MethodDefinition cctor =  new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.HideBySig |
                                                            MethodAttributes.SpecialName | MethodAttributes.RTSpecialName |
                                                            MethodAttributes.Static, mod.Import(typeof(void)));
            mod.GetType("<Module>").Methods.Add(cctor);
            MethodBody bdy = cctor.Body = new MethodBody(cctor);
            ILProcessor psr = bdy.GetILProcessor();
            psr.Emit(OpCodes.Call, mod.Import(typeof(AppDomain).GetProperty("CurrentDomain").GetGetMethod()));
            psr.Emit(OpCodes.Ldnull);
            psr.Emit(OpCodes.Ldftn, t.Methods.FirstOrDefault(mtd => mtd.Name == "DecryptAsm"));
            psr.Emit(OpCodes.Newobj, mod.Import(typeof(ResolveEventHandler).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) })));
            psr.Emit(OpCodes.Callvirt, mod.Import(typeof(AppDomain).GetEvent("AssemblyResolve").GetAddMethod()));
            psr.Emit(OpCodes.Ret);

            MethodDefinition main = t.Methods.FirstOrDefault(mtd => mtd.Name == "Main");
            mod.EntryPoint = main;
        }
        static string GetNewName(string n, int key)
        {
            byte[] b = Encoding.UTF8.GetBytes(n);
            for (int i = 0; i < b.Length; i++)
                b[i] = (byte)(b[i] ^ key ^ i);
            return Encoding.UTF8.GetString(b);
        }
        static byte[] Encrypt(byte[] asm, int key)
        {
            MemoryStream str = new MemoryStream();
            using (BinaryWriter wtr = new BinaryWriter(new DeflateStream(str, CompressionMode.Compress)))
            {
                wtr.Write(asm.Length);
                wtr.Write(asm);
            }
            byte[] ret = str.ToArray();
            for (int i = 0; i < ret.Length; i++)
            {
                ret[i] = (byte)(ret[i] ^ i ^ key);
            }
            return ret;
        }
        static byte[] Decrypt(byte[] asm, int key)
        {
            for (int i = 0; i < asm.Length; i++)
            {
                asm[i] = (byte)(asm[i] ^ i ^ key);
            }
            DeflateStream str = new DeflateStream(new MemoryStream(asm), CompressionMode.Decompress);
            using (BinaryReader rdr = new BinaryReader(str))
            {
                byte[] ret = new byte[rdr.ReadInt32()];
                byte[] over = new byte[0x100];
                int i;
                for (i = 0; i + 0x100 < ret.Length; i += 0x100)
                {
                    byte[] b = rdr.ReadBytes(0x100);
                    Buffer.BlockCopy(b, 0, ret, i, 0x100);
                    Buffer.BlockCopy(over, 0, b, 0, 0x100);
                }
                if (i != ret.Length)
                {
                    int re = ret.Length - i;
                    byte[] b = rdr.ReadBytes(re);
                    Buffer.BlockCopy(b, 0, ret, i, re);
                    Buffer.BlockCopy(over, 0, b, 0, re);
                }
                return ret;
            }
        }
    }
}
