﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Roslyn.Utilities;

namespace Microsoft.Cci
{
    internal abstract class ReferenceIndexer : ReferenceIndexerBase
    {
        protected readonly MetadataWriter metadataWriter;
        private readonly HashSet<IImportScope> _alreadySeenScopes = new HashSet<IImportScope>();

        internal ReferenceIndexer(MetadataWriter metadataWriter)
            : base(metadataWriter.Context)
        {
            this.metadataWriter = metadataWriter;
        }

        public override void Visit(IAssembly assembly)
        {
            this.module = assembly;
            this.Visit((IModule)assembly);
            this.Visit(assembly.GetFiles(Context));
            this.Visit(assembly.GetResources(Context));
        }

        public override void Visit(IModule module)
        {
            this.module = module;

            //EDMAURER visit these assembly-level attributes even when producing a module.
            //They'll be attached off the "AssemblyAttributesGoHere" typeRef if a module is being produced.

            this.Visit(module.AssemblyAttributes);
            this.Visit(module.AssemblySecurityAttributes);

            this.Visit(module.GetAssemblyReferences(Context));
            this.Visit(module.ModuleReferences);
            this.Visit(module.ModuleAttributes);
            this.Visit(module.GetTopLevelTypes(Context));
            this.Visit(module.GetExportedTypes(Context));

            if (module.AsAssembly == null)
            {
                this.Visit(module.GetResources(Context));
            }

            VisitImports(module.GetImports());
        }

        public void VisitMethodBodyReference(IReference reference)
        {
            var typeReference = reference as ITypeReference;
            if (typeReference != null)
            {
                this.typeReferenceNeedsToken = true;
                this.Visit(typeReference);
                Debug.Assert(!this.typeReferenceNeedsToken);
            }
            else
            {
                var fieldReference = reference as IFieldReference;
                if (fieldReference != null)
                {
                    if (fieldReference.IsContextualNamedEntity)
                    {
                        ((IContextualNamedEntity)fieldReference).AssociateWithMetadataWriter(this.metadataWriter);
                    }

                    this.Visit(fieldReference);
                }
                else
                {
                    var methodReference = reference as IMethodReference;
                    if (methodReference != null)
                    {
                        this.Visit(methodReference);
                    }
                }
            }
        }

        protected override void RecordAssemblyReference(IAssemblyReference assemblyReference)
        {
            this.metadataWriter.GetAssemblyRefIndex(assemblyReference);
        }

        protected override void ProcessMethodBody(IMethodDefinition method)
        {
            if (method.HasBody())
            {
                var body = method.GetBody(Context);

                if (body != null)
                {
                    this.Visit(body);

                    for (IImportScope scope = body.ImportScope; scope != null; scope = scope.Parent)
                    {
                        if (_alreadySeenScopes.Add(scope))
                        {
                            VisitImports(scope.GetUsedNamespaces());
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else if (!metadataWriter.allowMissingMethodBodies)
                {
                    throw ExceptionUtilities.Unreachable;
                }
            }
        }

        private void VisitImports(ImmutableArray<UsedNamespaceOrType> imports)
        {
            // Visit type and assembly references in import scopes.
            // These references are emitted to Portable debug metadata,
            // so they need to be present in the assembly metadata.
            // It may happen that some using/import clause references an assembly/type 
            // that is not actually used in IL. Although rare we need to handle such cases.
            // We include these references regardless of the format for debugging information 
            // to avoid dependency of metadata on the chosen debug format.

            foreach (var import in imports)
            {
                if (import.TargetAssemblyOpt != null)
                {
                    Visit(import.TargetAssemblyOpt);
                }

                if (import.TargetTypeOpt != null)
                {
                    this.typeReferenceNeedsToken = true;
                    Visit(import.TargetTypeOpt);
                    Debug.Assert(!this.typeReferenceNeedsToken);
                }
            }
        }

        protected override void RecordTypeReference(ITypeReference typeReference)
        {
            this.metadataWriter.RecordTypeReference(typeReference);
        }

        protected override void RecordTypeMemberReference(ITypeMemberReference typeMemberReference)
        {
            this.metadataWriter.GetMemberRefIndex(typeMemberReference);
        }

        protected override void RecordFileReference(IFileReference fileReference)
        {
            this.metadataWriter.GetFileRefIndex(fileReference);
        }

        protected override void ReserveMethodToken(IMethodReference methodReference)
        {
            this.metadataWriter.GetMethodToken(methodReference);
        }

        protected override void ReserveFieldToken(IFieldReference fieldReference)
        {
            this.metadataWriter.GetFieldToken(fieldReference);
        }

        protected override void RecordModuleReference(IModuleReference moduleReference)
        {
            this.metadataWriter.GetModuleRefIndex(moduleReference.Name);
        }

        public override void Visit(IPlatformInvokeInformation platformInvokeInformation)
        {
            this.metadataWriter.GetModuleRefIndex(platformInvokeInformation.ModuleName);
        }
    }
}
