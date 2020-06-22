// --------------------------------------------------------------------------------------------------------------------
// <copyright file="BatchEditor.cs" company="RHEA System S.A.">
//    Copyright (c) 2015-2020 RHEA System S.A.
//
//    Author: Sam Gerené, Alex Vorobiev, Merlin Bieze, Naron Phou, Patxi Ozkoidi, Alexander van Delft,
//            Nathanael Smiechowski, Kamil Wojnowski
//
//    This file is part of CDP4 Batch Editor. 
//    The CDP4 Batch Editor is the RHEA Concurrent Design Desktop Application and Excel Integration
//    compliant with ECSS-E-TM-10-25 Annex A and Annex C.
//
//    The CDP4 Batch Editor is free software; you can redistribute it and/or
//    modify it under the terms of the GNU Affero General Public
//    License as published by the Free Software Foundation; either
//    version 3 of the License, or any later version.
//
//    The CDP4 Batch Editor is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//    GNU Affero General Public License for more details.
//
//    You should have received a copy of the GNU Affero General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace CDP4BatchEditor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;

    using CDP4Common.EngineeringModelData;
    using CDP4Common.SiteDirectoryData;

    using CDP4Dal;
    using CDP4Dal.Permission;

    using CDP4ServicesDal;

    using Ocdt.Tools;

    using Enumerable = System.Linq.Enumerable;

    /// <inheritdoc />
    /// <summary>
    /// Batch editor command line tool to execute bulk changes on an CDP4 Persistent Data Store (PDS) via the Web Services
    /// Processor (WSP).
    /// </summary>
    public class BatchEditor : ToolBase
    {
        /// <summary>
        /// The short names of the Category filter. The requested action is only applied to ElementDefinitions that are a member of
        /// these categories.
        /// </summary>
        private readonly HashSet<string> filteredCategoryShortNames;

        /// <summary>
        /// The ElementDefinition filter. The requested action is only applied to ElementDefinitions in this set.
        /// </summary>
        private readonly HashSet<ElementDefinition> filteredElementDefinitions;

        /// <summary>
        /// The DomainOfExpertise owners filter. The requested action is only applied to ElementDefinitions owned by domains
        /// included in this set.
        /// </summary>
        private readonly HashSet<DomainOfExpertise> includedOwners;

        /// <summary>
        /// The command line options.
        /// </summary>
        private readonly Options options;

        /// <summary>
        /// The standard dimension parameter short names.
        /// </summary>
        private readonly string[] standardDimensionParameterShortNames = { "diam", "height", "len", "wid" };

        /// <inheritdoc />
        /// <summary>
        /// Initialises a new instance of the <see cref="BatchEditor" /> class.
        /// </summary>
        /// <param name="options">
        /// The options.
        /// </param>
        public BatchEditor(Options options)
        {
            this.options = options;
            this.filteredElementDefinitions = new HashSet<ElementDefinition>();
            this.filteredCategoryShortNames = new HashSet<string>();
            this.includedOwners = new HashSet<DomainOfExpertise>();
        }

        private static int RunOptionsAndReturnExitCode(Options parsedOptions)
        {
            var batchEditor = new BatchEditor(parsedOptions);
            batchEditor.Run();

            return 1;
        }

        private static void HandleParseError(IEnumerable<Error> errs)
        {
            //TODO Define how to handle exception here
        }

        /// <summary>
        /// Run the batch editor.
        /// </summary>
        public void Run()
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            // Assign the permission service

            Logger.Info($"==== CDP4 Batch Editor V{typeof(App).Assembly.GetName().Version} ====");
            Logger.Info($"BatchEditor started with options: {OptionsBase.SummarizeOptions(this.options)}");
            Logger.Info("Use --help to get usage information.");
            Logger.Warn("BE VERY CAREFUL with BatchEditor, because you can damage your Engineering models with it! If you are not sure, QUIT NOW by hitting control-C.");

            if (this.OpenSession(this.options.Server))
            {
                var engineeringModelShortName = this.options.Model;
                this.ProcessEngineeringModel(engineeringModelShortName);
            }

            stopWatch.Stop();
            Logger.Info("BatchEditor in {0} s", stopWatch.ElapsedMilliseconds / 1000.0);

#if DEBUG

            // Prevent the command prompt window from closing when debugging
            ConsoleAppHelpers.HitAnyKeyTo("exit");
#endif
        }

        /// <summary>
        /// Process the <see cref="EngineeringModel" /> with the given short name.
        /// </summary>
        /// <param name="engineeringModelShortName">
        /// The short name of the <see cref="EngineeringModelSetup" /> and <see cref="EngineeringModel" /> to process.
        /// If the short name is "*" all engineering models in the persistent data store will be processed.
        /// </param>
        private void ProcessEngineeringModel(string engineeringModelShortName)
        {
            foreach (var engineeringModelSetup in this.GetEngineeringModelSetups(engineeringModelShortName))
            {
                var iteration = this.OpenLastIterationOfEngineeringModel(engineeringModelSetup);

                if (iteration != null)
                {
                    var selectedParameters = this.options.SelectedParameters == null
                        ? new List<string>()
                        : Enumerable.ToList(Enumerable.Select(this.options.SelectedParameters, shortName => shortName.Trim()));

                    this.filteredCategoryShortNames.Clear();

                    if (this.options.FilteredCategories != null)
                    {
                        foreach (var shortName in this.options.FilteredCategories)
                        {
                            this.filteredCategoryShortNames.Add(shortName.Trim());
                        }
                    }

                    this.filteredElementDefinitions.Clear();

                    if (string.IsNullOrWhiteSpace(this.options.FilteredSubTree))
                    {
                        this.filteredElementDefinitions.AddAll(iteration.Element);
                    }
                    else
                    {
                        var topOfSubTreeShortName = this.options.FilteredSubTree.Trim();

                        var topOfSubTree =
                            iteration.Element.FirstOrDefault(ed => ed.ShortName == topOfSubTreeShortName);

                        if (topOfSubTree == null)
                        {
                            Logger.Warn($"Cannot find Element Definition with short name {topOfSubTreeShortName} for --filtered-subtree");
                        }
                        else
                        {
                            this.CollectSubTreeElementDefinitions(topOfSubTree, this.filteredElementDefinitions);
                        }
                    }

                    this.includedOwners.Clear();

                    if (this.options.IncludedOwners == null)
                    {
                        this.includedOwners.AddAll(engineeringModelSetup.ActiveDomain);
                    }
                    else
                    {
                        this.includedOwners.AddAll(engineeringModelSetup.ActiveDomain.Where(d => this.options.IncludedOwners.Contains(d.ShortName)));
                    }

                    if (this.options.ExcludedOwners != null)
                    {
                        this.includedOwners.RemoveWhere(d => this.options.ExcludedOwners.Contains(d.ShortName));
                    }

                    Logger.Info($"Included owners filter: {string.Join(", ", this.includedOwners.Select(d => d.ShortName).OrderBy(s => s))}");

                    // Perform the requested action
                    if (!string.IsNullOrWhiteSpace(this.options.Action))
                    {
                        switch (this.options.Action.ToLower())
                        {
                            case "add-parameters":
                                if (selectedParameters.Count > 0)
                                {
                                    AddParameters(
                                        iteration,
                                        selectedParameters,
                                        this.options.ParameterGroup,
                                        this.options.DomainOfExpertise);
                                }

                                break;

                            case "remove-parameters":
                                if (selectedParameters.Count > 0)
                                {
                                    RemoveParameters(
                                        iteration,
                                        selectedParameters,
                                        this.options.DomainOfExpertise);
                                }

                                break;

                            case "move-ref-to-manual-values":
                                MoveRefToManualValues(iteration);
                                break;

                            case "apply-option-dependence":
                                ApplyOptionDependenceToParameters(iteration, selectedParameters);
                                break;

                            case "remove-option-dependence":
                                RemoveOptionDependenceFromParameters(iteration, selectedParameters);
                                break;

                            case "apply-state-dependence":
                                if (!string.IsNullOrWhiteSpace(this.options.StateList) && selectedParameters.Count > 0)
                                {
                                    ApplyStateDependenceToParameters(iteration, this.options.StateList, selectedParameters);
                                }

                                break;

                            case "remove-state-dependence":
                                if (!string.IsNullOrWhiteSpace(this.options.StateList) && selectedParameters.Count > 0)
                                {
                                    RemoveStateDependenceFromParameters(
                                        iteration,
                                        this.options.StateList,
                                        selectedParameters);
                                }

                                break;

                            case "set-shape-scale-mm":
                                StandardizeDimensionsInMillimetre(iteration, this.standardDimensionParameterShortNames);
                                break;

                            case "set-scale":
                                var measurementScale = string.IsNullOrWhiteSpace(this.options.Scale)
                                    ? null
                                    : iteration.AggregateReferenceDataLibrary.Scale.FirstOrDefault(x => x.ShortName == this.options.Scale);

                                if (measurementScale == null)
                                {
                                    Logger.Warn("Invalid action \"set-scale\": short name of a valid scale must be given in --scale.");
                                    break;
                                }

                                if (selectedParameters.Count > 0)
                                {
                                    AssignMeasurementScale(iteration, selectedParameters, measurementScale);
                                }

                                break;

                            case "set-subscription-switch":
                                if (!string.IsNullOrWhiteSpace(this.options.DomainOfExpertise)
                                    && !string.IsNullOrWhiteSpace(this.options.SubscriptionSwitch))
                                {
                                    ParameterSwitchKind parameterSwitchKind;

                                    if (Enum.TryParse(this.options.SubscriptionSwitch.Trim(), true, out parameterSwitchKind))
                                    {
                                        var subscriber =
                                            this.SiteDirectory.Domain.FirstOrDefault(
                                                domainOfExpertise =>
                                                    domainOfExpertise.ShortName == this.options.DomainOfExpertise);

                                        SetParameterSubscriptionsSwitch(iteration, subscriber, parameterSwitchKind);
                                    }
                                    else
                                    {
                                        Logger.Error(
                                            "Invalid --subscription-switch={0}. Should be Computed, Manual or Reference.",
                                            this.options.SubscriptionSwitch);
                                    }
                                }

                                break;

                            case "standardise-groups":
                                StandardiseParameterGroups(iteration);
                                break;

                            case "standardise-for-cdf":
                                StandardiseModelForCdf(iteration);
                                break;

                            case "standardise-basic-parameters":
                                StandardiseModelWithBasicParameters(iteration);
                                break;

                            case "set-generic-owners":
                                SetGenericEquipmentOwnership(iteration);
                                break;

                            case "change-parameter-ownership":
                                ChangeParameterOwnership(iteration, selectedParameters, this.options.DomainOfExpertise);
                                break;

                            case "change-domain":
                                var fromDomain = this.options.DomainOfExpertise;
                                var toDomain = this.options.ToDomainOfExpertise;

                                if (fromDomain == null)
                                {
                                    fromDomain = "";
                                }

                                if (toDomain == null)
                                {
                                    toDomain = "";
                                }

                                ChangeDomain(iteration, fromDomain, toDomain);
                                break;

                            case "subscribe":
                                if (!string.IsNullOrWhiteSpace(this.options.DomainOfExpertise) &&
                                    selectedParameters.Count > 0)
                                {
                                    var subscriber =
                                        this.SiteDirectory.Domain.FirstOrDefault(
                                            domainOfExpertise =>
                                                domainOfExpertise.ShortName == this.options.DomainOfExpertise);

                                    SubscribeParameters(iteration, subscriber, selectedParameters);
                                }

                                break;

                            default:
                                Logger.Error($"Invalid --action={this.options.Action}. Use --help to get usage information.");
                                break;
                        }
                    }

                    if (this.options.Report)
                    {
                        ReportParameters(iteration);
                    }

                    ObjStore.CloseEngineeringModel(iteration.ContainerEngineeringModel);

                    if (this.options.DryRun)
                    {
                        Logger.Info($"This was a dry run. Nothing was actually changed in model {engineeringModelSetup.ShortName}.");
                    }
                }
            }
        }

        /// <summary>
        /// Collect all Element Definitions contained in the subtree of a given top Element Definition.
        /// </summary>
        /// <param name="topOfSubTree">
        /// The top <see cref="ElementDefinition" /> of a subtree to be derived.
        /// </param>
        /// <param name="subTreeElementDefinitions">
        /// Set to store to the subtree Element Definitions.
        /// </param>
        private void CollectSubTreeElementDefinitions(
            ElementDefinition topOfSubTree,
            HashSet<ElementDefinition> subTreeElementDefinitions)
        {
            subTreeElementDefinitions.Add(topOfSubTree);

            foreach (var elementUsage in topOfSubTree.ContainedElement)
            {
                subTreeElementDefinitions.Add(elementUsage.ElementDefinition);

                // Recursively add the lower level subtree elements
                this.CollectSubTreeElementDefinitions(elementUsage.ElementDefinition, subTreeElementDefinitions);
            }
        }

        /// <summary>
        /// Check whether the given <see cref="ElementDefinition" /> is a member of the specified selected categories.
        /// </summary>
        /// <param name="elementDefinition">
        /// The <see cref="ElementDefinition" /> to check.
        /// </param>
        /// <returns>
        /// True if no categories were specified or the given Element Definition is a member, otherwise false.
        /// </returns>
        public bool IsMemberOfSelectedCategory(ElementDefinition elementDefinition)
        {
            if (this.filteredCategoryShortNames.None())
            {
                return true;
            }

            var elementCategoryShortNames =
                DomainModelHelpers.GetAllCategories(elementDefinition).Select(cat => cat.ShortName);

            return Enumerable.Any(Enumerable.Intersect(this.filteredCategoryShortNames, elementCategoryShortNames));
        }

        /// <summary>
        /// Check whether the given <see cref="ElementDefinition" /> is included in the filter.
        /// </summary>
        /// <param name="elementDefinition">
        /// The <see cref="ElementDefinition" /> to check.
        /// </param>
        /// <returns>
        /// If included returns true, otherwise false.
        /// </returns>
        private bool IsFilteredIn(ElementDefinition elementDefinition)
        {
            return this.filteredElementDefinitions.Contains(elementDefinition) && this.IsMemberOfSelectedCategory(elementDefinition)
                                                                               && this.includedOwners.Contains(
                                                                                   elementDefinition
                                                                                       .Owner);
        }

        /// <summary>
        /// Check whether the given <see cref="ElementDefinition" /> is excluded from the filter.
        /// </summary>
        /// <param name="elementDefinition">
        /// The <see cref="ElementDefinition" /> to check.
        /// </param>
        /// <returns>
        /// If included returns true, otherwise false.
        /// </returns>
        private bool IsFilteredOut(ElementDefinition elementDefinition)
        {
            return !this.IsFilteredIn(elementDefinition);
        }

        /// <summary>
        /// Edit the given <see cref="Iteration" />.
        /// </summary>
        /// <param name="iteration">
        /// The <see cref="Iteration" /> to edit.
        /// </param>
        private void EditIteration(Iteration iteration)
        {
            var unitOfWork = new UnitOfWork();

            ////this.RenameParameterGroups(iteration, unitOfWork);
            this.ReplaceParameterOwnerShip(iteration, unitOfWork);

            if (!this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Standardise the engineering model iteration according to CDF standards.
        /// </summary>
        /// <param name="iteration">
        /// The <see cref="Iteration" /> to standardise.
        /// </param>
        private void StandardiseModelForCdf(Iteration iteration)
        {
            var unitOfWork = new UnitOfWork();

            // Rename all usages to have the name and short name of its associated element definition
            var usageTotalsMap = new Dictionary<ElementDefinition, int>();
            var usageNumbersMap = new Dictionary<ElementDefinition, int>();

            foreach (var elementDefinition in iteration.Element.OrderBy(
                definition => definition.Name,
                new CaseInsensitiveStringComparer()))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                usageTotalsMap.Clear();
                usageNumbersMap.Clear();

                foreach (var elementUsage in elementDefinition.ContainedElement)
                {
                    if (usageTotalsMap.ContainsKey(elementUsage.ElementDefinition))
                    {
                        usageTotalsMap[elementUsage.ElementDefinition] += 1;
                    }
                    else
                    {
                        usageTotalsMap[elementUsage.ElementDefinition] = 1;
                        usageNumbersMap[elementUsage.ElementDefinition] = 1;
                    }
                }

                foreach (var elementUsage in elementDefinition.ContainedElement.OrderBy(
                    usage => usage.Name,
                    new CaseInsensitiveStringComparer()))
                {
                    var numberOfUsagesOfSameDefinition = usageTotalsMap[elementUsage.ElementDefinition];
                    var numberOfDigits = Utils.ComputeNumberOfDigits(numberOfUsagesOfSameDefinition);

                    if (numberOfUsagesOfSameDefinition != 1 || elementUsage.Name != elementUsage.ElementDefinition.Name
                                                            || elementUsage.ShortName !=
                                                            elementUsage.ElementDefinition.ShortName)
                    {
                        var mod = unitOfWork.GetModifiableDomainObject(elementUsage);

                        if (numberOfUsagesOfSameDefinition == 1)
                        {
                            mod.Name = elementUsage.ElementDefinition.Name;
                            mod.ShortName = elementUsage.ElementDefinition.ShortName;
                        }
                        else
                        {
                            var usageNumberString =
                                usageNumbersMap[elementUsage.ElementDefinition].ToString(CultureInfo.InvariantCulture)
                                    .PadLeft(numberOfDigits, '0');

                            mod.Name = string.Format(
                                "{0} #{1}",
                                elementUsage.ElementDefinition.Name,
                                usageNumberString);

                            mod.ShortName = string.Format(
                                "{0}_{1}",
                                elementUsage.ElementDefinition.ShortName,
                                usageNumberString);

                            usageNumbersMap[elementUsage.ElementDefinition] += 1;
                        }

                        if (elementUsage.Name != mod.Name || elementUsage.ShortName != mod.ShortName)
                        {
                            Logger.Info(
                                "Renamed usage from {0} ({1}) to {2} ({3})",
                                elementUsage.Name,
                                elementUsage.ShortName,
                                mod.Name,
                                mod.ShortName);
                        }
                    }
                }
            }

            if (!this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();

            this.StandardiseParameterGroups(iteration);

            this.StandardizeDimensionsInMillimetre(iteration, this.standardDimensionParameterShortNames);
        }

        /// <summary>
        /// Standardise the engineering model iteration according to Triton X standards.
        /// </summary>
        /// <param name="iteration">
        /// The <see cref="Iteration" /> to standardise.
        /// </param>
        private void StandardiseModelWithBasicParameters(Iteration iteration)
        {
            var unitOfWork = new UnitOfWork();
            this.StandardizeParametersTritonX(iteration, unitOfWork);

            if (!this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Report all parameters on the given <see cref="Iteration" />.
        /// </summary>
        /// <param name="iteration">
        /// The <see cref="Iteration" /> to report.
        /// </param>
        /// <param name="unitOfWork">
        /// The unit Of Work.
        /// </param>
        private void ReplaceParameterOwnerShip(Iteration iteration, UnitOfWork unitOfWork)
        {
            var syeOwner = DomainObjectStore.Singleton.GetSiteDirectory().Domain.Single(x => x.ShortName == "SYE");
            var targetOwner = DomainObjectStore.Singleton.GetSiteDirectory().Domain.Single(x => x.ShortName == "STR");

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                if (elementDefinition.Owner == targetOwner)
                {
                    foreach (var parameter in elementDefinition.Parameter)
                    {
                        if (parameter.ParameterType.ShortName == "P_mean" ||
                            parameter.ParameterType.ShortName == "P_duty_cyc")
                        {
                            if (parameter.Owner != syeOwner)
                            {
                                var mod = unitOfWork.GetModifiableDomainObject(parameter);
                                mod.Owner = syeOwner;

                                Logger.Info(
                                    "Changed owner of {0}.{1} from {2} to {3}",
                                    elementDefinition.ShortName,
                                    parameter.ParameterType.ShortName,
                                    parameter.Owner.ShortName,
                                    mod.Owner.ShortName);
                            }
                        }
                        else if (parameter.Owner != targetOwner)
                        {
                            var mod = unitOfWork.GetModifiableDomainObject(parameter);
                            mod.Owner = targetOwner;

                            Logger.Info(
                                "Changed owner of {0}.{1} from {2} to {3}",
                                elementDefinition.ShortName,
                                parameter.ParameterType.ShortName,
                                parameter.Owner.ShortName,
                                mod.Owner.ShortName);
                        }
                    }
                }

                foreach (var elementUsage in elementDefinition.ContainedElement)
                {
                    if (elementUsage.Owner == targetOwner)
                    {
                        foreach (var parameterOverride in elementUsage.ParameterOverride)
                        {
                            if (parameterOverride.ParameterType.ShortName == "P_mean" ||
                                parameterOverride.ParameterType.ShortName == "P_duty_cyc")
                            {
                                if (parameterOverride.Owner != syeOwner)
                                {
                                    var mod = unitOfWork.GetModifiableDomainObject(parameterOverride);
                                    mod.Owner = syeOwner;

                                    Logger.Info(
                                        "Changed owner of {0}.{1} from {2} to {3}",
                                        elementDefinition.ShortName,
                                        parameterOverride.ParameterType.ShortName,
                                        parameterOverride.Owner.ShortName,
                                        mod.Owner.ShortName);
                                }
                            }
                            else if (parameterOverride.Owner != targetOwner &&
                                     parameterOverride.ParameterType.ShortName != "loc")
                            {
                                var mod = unitOfWork.GetModifiableDomainObject(parameterOverride);
                                mod.Owner = targetOwner;

                                Logger.Info(
                                    "Changed owner of {0}.{1} from {2} to {3}",
                                    elementUsage.ShortName,
                                    parameterOverride.ParameterType.ShortName,
                                    parameterOverride.Owner.ShortName,
                                    mod.Owner.ShortName);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remove parameter groups from element definitions that are not categorised as "Equipment" or "Components".
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        private void RemoveParameterGroupsFromOtherThanEquipmentComponents(Iteration iteration)
        {
            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                var isEquipment = Enumerable.Any(elementDefinition.Category, category => category.ShortName == "Equipment");
                var isComponent = Enumerable.Any(elementDefinition.Category, category => category.ShortName == "Components");

                if (isEquipment || isComponent)
                {
                    continue;
                }

                var groupCostProgRisk = this.GetOrCreateParameterGroup(elementDefinition, "Cost_Prog_Risk", unitOfWork);
                var groupGeometry = this.GetOrCreateParameterGroup(elementDefinition, "Geometry", unitOfWork);
                var groupMass = this.GetOrCreateParameterGroup(elementDefinition, "Mass", unitOfWork);
                var groupPower = this.GetOrCreateParameterGroup(elementDefinition, "Power", unitOfWork);
                var groupThermal = this.GetOrCreateParameterGroup(elementDefinition, "Thermal", unitOfWork);

                var modElementDefinition = unitOfWork.GetModifiableDomainObject(elementDefinition);
                modElementDefinition.ParameterGroup.Remove(groupCostProgRisk);
                modElementDefinition.ParameterGroup.Remove(groupGeometry);
                modElementDefinition.ParameterGroup.Remove(groupMass);
                modElementDefinition.ParameterGroup.Remove(groupPower);
                modElementDefinition.ParameterGroup.Remove(groupThermal);
            }

            if (!this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Standardize the grouping of common parameters in CDF engineering models.
        /// </summary>
        /// <param name="iteration">
        /// The iteration to standardize.
        /// </param>
        private void StandardizeGroupingOfCommonParameters(Iteration iteration)
        {
            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                var allCategories = DomainModelHelpers.GetAllCategories(elementDefinition);
                var isEquipment = allCategories.Any(category => category.ShortName == "Equipment");
                var isComponent = allCategories.Any(category => category.ShortName == "Components");
                var isInstrument = allCategories.Any(category => category.ShortName == "Instruments");

                if (!(isEquipment || isComponent || isInstrument))
                {
                    continue;
                }

                var groupCostProgRisk = this.GetOrCreateParameterGroup(elementDefinition, "Cost_Prog_Risk", unitOfWork);
                var groupGeometry = this.GetOrCreateParameterGroup(elementDefinition, "Geometry", unitOfWork);
                var groupMass = this.GetOrCreateParameterGroup(elementDefinition, "Mass", unitOfWork);
                var groupPower = this.GetOrCreateParameterGroup(elementDefinition, "Power", unitOfWork);
                var groupThermal = this.GetOrCreateParameterGroup(elementDefinition, "Thermal", unitOfWork);

                this.MoveParameterToGroup(elementDefinition, "m", groupMass, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "mass_margin", groupMass, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "mass_dry", groupMass, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "mass_wet", groupMass, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "shape", groupGeometry, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "len", groupGeometry, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "loc", groupGeometry, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "wid", groupGeometry, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "height", groupGeometry, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "diam", groupGeometry, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "TRL", groupCostProgRisk, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "P_on", groupPower, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "P_stby", groupPower, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "P_duty_cyc", groupPower, unitOfWork);
                this.MoveParameterToGroup(elementDefinition, "P_mean", groupPower, unitOfWork);
            }

            if (!this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Standardize common parameters in the TRITONX model.
        /// </summary>
        /// <param name="iteration">
        /// The iteration to standardize.
        /// </param>
        /// <param name="unitOfWork">
        /// The active unit of work.
        /// </param>
        private void StandardizeParametersTritonX(Iteration iteration, UnitOfWork unitOfWork)
        {
            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                var allCategories = DomainModelHelpers.GetAllCategories(elementDefinition);
                var isEquipment = allCategories.Any(category => category.ShortName == "Equipment");
                var isComponent = allCategories.Any(category => category.ShortName == "Components");
                var isInstrument = allCategories.Any(category => category.ShortName == "Instruments");

                if (!(isEquipment || isComponent || isInstrument))
                {
                    continue;
                }

                var groupCostProgRisk = this.GetOrCreateParameterGroup(elementDefinition, "Cost_Prog_Risk", unitOfWork);
                var groupGeometry = this.GetOrCreateParameterGroup(elementDefinition, "Geometry", unitOfWork);
                var groupMass = this.GetOrCreateParameterGroup(elementDefinition, "Mass", unitOfWork);
                var groupPower = this.GetOrCreateParameterGroup(elementDefinition, "Power", unitOfWork);
                var groupThermal = this.GetOrCreateParameterGroup(elementDefinition, "Thermal", unitOfWork);

                this.EnsureStandardParameter(iteration, elementDefinition, "m", "kg", groupMass, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "mass_margin", "%", groupMass, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "P_on", "W", groupPower, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "P_stby", "W", groupPower, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "P_mean", "W", groupPower, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "P_peak", "W", groupPower, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "P_duty_cyc", "-1...1", groupPower, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "eff_thermal", "-", groupPower, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "len", "mm", groupGeometry, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "height", "mm", groupGeometry, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "wid", "mm", groupGeometry, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "Vol", "m³", groupGeometry, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "TRL", null, groupCostProgRisk, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "cost_acq", "k€", groupCostProgRisk, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "T_nonop_max", "°C", groupThermal, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "T_nonop_min", "°C", groupThermal, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "T_op_max", "°C", groupThermal, unitOfWork);
                this.EnsureStandardParameter(iteration, elementDefinition, "T_op_min", "°C", groupThermal, unitOfWork);
            }
        }

        /// <summary>
        /// Get parameter group with given name. If it does not exist create it.
        /// </summary>
        /// <param name="elementDefinition">
        /// The element definition.
        /// </param>
        /// <param name="parameterGroupName">
        /// The parameter group name.
        /// </param>
        /// <param name="unitOfWork">
        /// The active <see cref="UnitOfWork" />.
        /// </param>
        /// <returns>
        /// The <see cref="ParameterGroup" />.
        /// </returns>
        private ParameterGroup GetOrCreateParameterGroup(
            ElementDefinition elementDefinition,
            string parameterGroupName,
            UnitOfWork unitOfWork)
        {
            var parameterGroup = Enumerable.SingleOrDefault(elementDefinition.ParameterGroup, pg => pg.Name == parameterGroupName);

            if (parameterGroup == null)
            {
                parameterGroup =
                    unitOfWork.CreateParameterGroup(name: parameterGroupName, container: elementDefinition);
            }

            return parameterGroup;
        }

        /// <summary>
        /// Move the parameter with given short name to the given parameter group.
        /// </summary>
        /// <param name="elementDefinition">
        /// The element definition.
        /// </param>
        /// <param name="parameterShortName">
        /// The short name of the <see cref="ParameterType" /> of the <see cref="Parameter" /> to move.
        /// </param>
        /// <param name="group">
        /// The <see cref="ParameterGroup" />.
        /// </param>
        /// <param name="unitOfWork">
        /// The active <see cref="UnitOfWork" />.
        /// </param>
        private void MoveParameterToGroup(
            ElementDefinition elementDefinition,
            string parameterShortName,
            ParameterGroup group,
            UnitOfWork unitOfWork)
        {
            if (group == null)
            {
                return;
            }

            var parameter =
                Enumerable.SingleOrDefault(elementDefinition.Parameter, x => x.ParameterType.ShortName == parameterShortName);

            if (parameter != null && parameter.Group != group)
            {
                var mod = unitOfWork.GetModifiableDomainObject(parameter);
                mod.Group = group;

                Logger.Info(
                    "In {0} moved parameter {1} to group {2}",
                    elementDefinition.ShortName,
                    parameter.ParameterType.ShortName,
                    group.Name);
            }
        }

        /// <summary>
        /// Ensure the parameter with given short name exists in the given parameter group.
        /// Create the parameter if it does not exist.
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        /// <param name="elementDefinition">
        /// The element definition.
        /// </param>
        /// <param name="parameterShortName">
        /// The short name of the <see cref="ParameterType" /> of the <see cref="Parameter" /> to move.
        /// </param>
        /// <param name="scaleShortName">
        /// The short name of the measurement scale, if applicable.
        /// </param>
        /// <param name="group">
        /// The <see cref="ParameterGroup" />.
        /// </param>
        /// <param name="unitOfWork">
        /// The active <see cref="UnitOfWork" />.
        /// </param>
        private void EnsureStandardParameter(
            Iteration iteration,
            ElementDefinition elementDefinition,
            string parameterShortName,
            string scaleShortName,
            ParameterGroup group,
            UnitOfWork unitOfWork)
        {
            var aggregateRdl = iteration.AggregateReferenceDataLibrary;

            var parameter =
                Enumerable.SingleOrDefault(elementDefinition.Parameter, p => p.ParameterType.ShortName == parameterShortName);

            if (parameter == null)
            {
                var parameterType =
                    aggregateRdl.ParameterType.SingleOrDefault(pt => pt.ShortName == parameterShortName);

                if (parameterType == null)
                {
                    Logger.Error($"Cannot find Parameter Type {parameterShortName}");
                }
                else
                {
                    var modElementDefinition = unitOfWork.GetModifiableDomainObject(elementDefinition);

                    parameter = unitOfWork.CreateParameter(
                        owner: modElementDefinition.Owner,
                        parameterType: parameterType,
                        container: modElementDefinition);
                }
            }

            if (parameter != null)
            {
                var mod = unitOfWork.GetModifiableDomainObject(parameter);

                if (group != null)
                {
                    mod.Group = group;
                }

                var scale = aggregateRdl.Scale.SingleOrDefault(s => s.ShortName == scaleShortName);

                if (parameter.ParameterType is QuantityKind && scale != null)
                {
                    parameter.Scale = scale;
                }

                var scaleInfo = scale == null ? "" : $" in [{scale.ShortName}]";
                Logger.Info($"In {elementDefinition.ShortName} ensured existence of parameter {parameter.ParameterType.ShortName}{scaleInfo} in group {group.Name}");
            }
        }

        /// <summary>
        /// Standardise the parameter groups for a CDF engineering model.
        /// </summary>
        /// <param name="iteration">
        /// The iteration to standardize.
        /// </param>
        private void StandardiseParameterGroups(Iteration iteration)
        {
            var groupRenameMap = new Dictionary<string, string>
            {
                { "Cost_Programmatics_Risk", "Cost_Prog_Risk" }, { "Dimensions", "Geometry" },
                { "Equipment Generic Parameter", "Generic" }, { "Equipment Generic Parameters", "Generic" },
                { "Equipment Specific Parameter", "Specific" }, { "Equipment Specific Parameters", "Specific" },
                { "Performance Parameters", "Performance" }, { "Risk, Cost and Programmatics", "Cost_Prog_Risk" },
                { "Risk_Cost_Programmatics", "Cost_Prog_Risk" }, { "Study Specific Parameter", "Study_Specific" },
                { "Study Specific Parameters", "Study_Specific" }, { "Study Specific", "Study_Specific" },
                { "Temperature", "Thermal" }
            };

            var parameterGroupNamesToDelete = new[] { "Generic", "Specific", "Study_Specific" };

            this.RenameParameterGroups(iteration, groupRenameMap);
            this.UngroupParameterGroups(iteration, parameterGroupNamesToDelete);
            this.DeleteParameterGroups(iteration, parameterGroupNamesToDelete);
            this.StandardizeGroupingOfCommonParameters(iteration);
        }

        /// <summary>
        /// Set the ownership of all parameters inside element definitions that have a name starting with "Generic Equipment".
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        private void SetGenericEquipmentOwnership(Iteration iteration)
        {
            var unitOfWork = new UnitOfWork();

            var pwrDomain = ObjStore.GetSiteDirectory().Domain
                .SingleOrDefault(domainOfExpertise => domainOfExpertise.ShortName == "PWR");

            var syeDomain = ObjStore.GetSiteDirectory().Domain
                .SingleOrDefault(domainOfExpertise => domainOfExpertise.ShortName == "SYE");

            var confDomain = ObjStore.GetSiteDirectory().Domain
                .SingleOrDefault(domainOfExpertise => domainOfExpertise.ShortName == "CONF");

            var prescribedOwnership = new Dictionary<string, DomainOfExpertise>
            {
                ["P_mean"] = syeDomain, ["P_duty_cyc"] = syeDomain, ["loc"] = confDomain
            };

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                var elementDefinitionOwner = elementDefinition.Owner;

                if (elementDefinition.Name.StartsWith("Generic Equipment"))
                {
                    foreach (var parameter in elementDefinition.Parameter)
                    {
                        DomainOfExpertise newOwner = null;

                        if (prescribedOwnership.TryGetValue(parameter.ParameterType.ShortName, out newOwner))
                        {
                            // Change ownership only if different from what is prescribed
                            if (parameter.Owner == newOwner)
                            {
                                newOwner = null;
                            }
                        }
                        else if (parameter.Owner != elementDefinitionOwner)
                        {
                            newOwner = elementDefinitionOwner;
                        }

                        // Change ownership if needed
                        if (newOwner != null)
                        {
                            var mod = unitOfWork.GetModifiableDomainObject(parameter);
                            mod.Owner = newOwner;
                            Logger.Info($"Changed owner of {parameter.Path} from {parameter.Owner.ShortName} to {newOwner.ShortName}");
                        }
                    }
                }
            }

            if (!this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Changes ownership owned items from one domain to another.
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        /// <param name="fromDomainShortName">
        /// The domain to change from
        /// </param>
        /// <param name="toDomainShortName">
        /// The domain to change to
        /// </param>
        private void ChangeDomain(Iteration iteration, string fromDomainShortName, string toDomainShortName)
        {
            var unitOfWork = new UnitOfWork();

            if (fromDomainShortName == toDomainShortName)
            {
                Logger.Warn("The from and to domains are the same. No changes performed.");
                return;
            }

            var fromDomain =
                ObjStore.GetSiteDirectory().Domain.SingleOrDefault(
                    domainOfExpertise =>
                        domainOfExpertise.ShortName == fromDomainShortName);

            if (fromDomain == null)
            {
                Logger.Warn($"The from-domain {fromDomainShortName} cannot be found.");
                return;
            }

            var toDomain = ObjStore.GetSiteDirectory().Domain
                .SingleOrDefault(domainOfExpertise => domainOfExpertise.ShortName == toDomainShortName);

            if (toDomain == null)
            {
                Logger.Warn($"The to-domain {fromDomainShortName} cannot be found.");
                return;
            }

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                var elementDefinitionOwner = elementDefinition.Owner;

                if (elementDefinitionOwner == fromDomain)
                {
                    var modElementDefinition = unitOfWork.GetModifiableDomainObject(elementDefinition);
                    modElementDefinition.Owner = toDomain;
                    Logger.Info($"Changed owner of {elementDefinition.ShortName} from {fromDomain.ShortName} to {toDomain.ShortName}");
                }

                foreach (var parameter in elementDefinition.Parameter)
                {
                    if (parameter.Owner == fromDomain)
                    {
                        var modParameter = unitOfWork.GetModifiableDomainObject(parameter);
                        modParameter.Owner = toDomain;
                        Logger.Info($"Changed owner of {parameter.Path} from {fromDomain.ShortName} to {toDomain.ShortName}");
                    }

                    foreach (var subscription in parameter.ParameterSubscription)
                    {
                        if (subscription.Owner == fromDomain)
                        {
                            var modSubscription = unitOfWork.GetModifiableDomainObject(subscription);
                            modSubscription.Owner = toDomain;
                            Logger.Info($"Changed owner of {modSubscription.Path} from {fromDomain.ShortName} to {toDomain.ShortName}");
                        }
                    }

                    foreach (var usage in elementDefinition.ContainedElement)
                    {
                        var usageOwner = usage.Owner;

                        if (usageOwner == fromDomain)
                        {
                            var modUsage = unitOfWork.GetModifiableDomainObject(usage);
                            modUsage.Owner = toDomain;
                            Logger.Info($"Changed owner of {usage.ShortName} from {fromDomain.ShortName} to {toDomain.ShortName}");
                        }

                        foreach (var parameterOverride in usage.ParameterOverride)
                        {
                            if (parameterOverride.Owner == fromDomain)
                            {
                                var modParameterOverride = unitOfWork.GetModifiableDomainObject(parameterOverride);
                                modParameterOverride.Owner = toDomain;
                                Logger.Info($"Changed owner of {parameterOverride.Path} from {fromDomain.ShortName} to {toDomain.ShortName}");
                            }

                            foreach (var subscription in parameterOverride.ParameterSubscription)
                            {
                                if (subscription.Owner == fromDomain)
                                {
                                    var modSubscription = unitOfWork.GetModifiableDomainObject(subscription);
                                    modSubscription.Owner = toDomain;
                                    Logger.Info($"Changed owner of {modSubscription.Path} from {fromDomain.ShortName} to {toDomain.ShortName}");
                                }
                            }
                        }
                    }
                }

                if (!this.options.DryRun)
                {
                    this.WebServiceClient.Write(unitOfWork);
                }

                unitOfWork.Dispose();
            }
        }

        /// <summary>
        /// Set the ownership of all parameters inside element definitions that have a name starting with "Generic Equipment".
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        private void ChangeParameterOwnership(
            Iteration iteration,
            List<string> selectedParameters,
            string domainShortName)
        {
            var unitOfWork = new UnitOfWork();

            var newOwnerDomain = ObjStore.GetSiteDirectory().Domain
                .SingleOrDefault(domainOfExpertise => domainOfExpertise.ShortName == domainShortName);

            if (newOwnerDomain == null)
            {
                Logger.Warn($"Cannot find domain of expertise for {domainShortName}");
            }
            else
            {
                foreach (var elementDefinition in iteration.Element)
                {
                    if (this.IsFilteredOut(elementDefinition))
                    {
                        continue;
                    }

                    var elementDefinitionOwner = elementDefinition.Owner;

                    foreach (var parameter in elementDefinition.Parameter)
                    {
                        if (selectedParameters.Contains(parameter.ParameterType.ShortName))
                        {
                            if (parameter.Owner != newOwnerDomain)
                            {
                                // Change ownership if needed
                                var mod = unitOfWork.GetModifiableDomainObject(parameter);
                                mod.Owner = newOwnerDomain;
                                Logger.Info($"Changed owner of {parameter.Path} from {parameter.Owner.ShortName} to {newOwnerDomain.ShortName}");
                            }
                        }
                    }
                }

                if (!this.options.DryRun)
                {
                    this.WebServiceClient.Write(unitOfWork);
                }

                unitOfWork.Dispose();
            }
        }

        /// <summary>
        /// Standardize dimensions in millimetre for the given iteration.
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        /// <param name="dimensionParameterShortNames">
        /// The short names of the dimension parameters to standardize.
        /// </param>
        private void StandardizeDimensionsInMillimetre(
            Iteration iteration,
            IEnumerable<string> dimensionParameterShortNames)
        {
            var unitOfWork = new UnitOfWork();

            var m = this.SiteDirectory.GeneralSiteReferenceDataLibrary.Scale.Single(x => x.ShortName == "m");
            var cm = this.SiteDirectory.GeneralSiteReferenceDataLibrary.Scale.Single(x => x.ShortName == "cm");
            var mm = this.SiteDirectory.GeneralSiteReferenceDataLibrary.Scale.Single(x => x.ShortName == "mm");

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                foreach (var dimensionParameterShortName in dimensionParameterShortNames)
                {
                    this.ConvertDimensionParameter(elementDefinition, dimensionParameterShortName, m, cm, mm, unitOfWork);
                }
            }

            if (!this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Convert given dimension parameter.
        /// </summary>
        /// <param name="elementDefinition">
        /// The element definition.
        /// </param>
        /// <param name="parameterShortName">
        /// The short name of the dimension parameter.
        /// </param>
        /// <param name="m">
        /// Metre scale.
        /// </param>
        /// <param name="cm">
        /// Centimetre scale.
        /// </param>
        /// <param name="mm">
        /// Millimetre scale.
        /// </param>
        /// <param name="unitOfWork">
        /// The active <see cref="UnitOfWork" />.
        /// </param>
        private void ConvertDimensionParameter(
            ElementDefinition elementDefinition,
            string parameterShortName,
            MeasurementScale m,
            MeasurementScale cm,
            MeasurementScale mm,
            UnitOfWork unitOfWork)
        {
            this.ConvertParameterValueAndScale(elementDefinition, parameterShortName, m, mm, 1000.0, unitOfWork);
            this.ConvertParameterValueAndScale(elementDefinition, parameterShortName, cm, mm, 10.0, unitOfWork);
        }

        /// <summary>
        /// Convert a parameter value and scale.
        /// </summary>
        /// <param name="elementDefinition">
        /// The element definition.
        /// </param>
        /// <param name="parameterShortName">
        /// The short name of the parameter.
        /// </param>
        /// <param name="oldScale">
        /// The old scale.
        /// </param>
        /// <param name="newScale">
        /// The new scale.
        /// </param>
        /// <param name="conversionFactor">
        /// The conversion factor.
        /// </param>
        /// <param name="unitOfWork">
        /// The active <see cref="UnitOfWork" />.
        /// </param>
        private void ConvertParameterValueAndScale(
            ElementDefinition elementDefinition,
            string parameterShortName,
            MeasurementScale oldScale,
            MeasurementScale newScale,
            double conversionFactor,
            UnitOfWork unitOfWork)
        {
            var parameter =
                Enumerable.SingleOrDefault(elementDefinition.Parameter, x => x.ParameterType.ShortName == parameterShortName);

            if (parameter != null)
            {
                var quantityKind = parameter.ParameterType as QuantityKind;

                if (quantityKind != null)
                {
                    if (parameter.Scale == oldScale && quantityKind.AllPossibleScale.Contains(newScale))
                    {
                        var ownerShortName = parameter.Owner.ShortName;
                        var errorCount = 0;

                        foreach (var parameterValueSet in parameter.ValueSet)
                        {
                            var mod = unitOfWork.GetModifiableDomainObject(parameterValueSet);

                            if (mod.Computed.Count == 1 && errorCount == 0)
                            {
                                mod.Computed[0] = this.ConvertNumericValue(
                                    elementDefinition,
                                    parameterShortName,
                                    false,
                                    ownerShortName,
                                    mod.Computed[0],
                                    oldScale,
                                    newScale,
                                    conversionFactor,
                                    ref errorCount);
                            }

                            if (mod.Manual.Count == 1 && errorCount == 0)
                            {
                                mod.Manual[0] = this.ConvertNumericValue(
                                    elementDefinition,
                                    parameterShortName,
                                    false,
                                    ownerShortName,
                                    mod.Manual[0],
                                    oldScale,
                                    newScale,
                                    conversionFactor,
                                    ref errorCount);
                            }

                            if (mod.Reference.Count == 1 && errorCount == 0)
                            {
                                mod.Reference[0] = this.ConvertNumericValue(
                                    elementDefinition,
                                    parameterShortName,
                                    false,
                                    ownerShortName,
                                    mod.Reference[0],
                                    oldScale,
                                    newScale,
                                    conversionFactor,
                                    ref errorCount);
                            }
                        }

                        foreach (var parameterSubscription in parameter.ParameterSubscription)
                        {
                            var subscriber = parameterSubscription.Owner.ShortName;

                            foreach (var parameterSubscriptionValueSet in parameterSubscription.ValueSet)
                            {
                                var modifiedValueSet =
                                    unitOfWork.GetModifiableDomainObject(parameterSubscriptionValueSet);

                                if (modifiedValueSet.Computed.Count == 1 && errorCount == 0)
                                {
                                    modifiedValueSet.Computed[0] = this.ConvertNumericValue(
                                        elementDefinition,
                                        parameterShortName,
                                        true,
                                        subscriber,
                                        modifiedValueSet.Computed[0],
                                        oldScale,
                                        newScale,
                                        conversionFactor,
                                        ref errorCount);
                                }

                                if (modifiedValueSet.Manual.Count == 1 && errorCount == 0)
                                {
                                    modifiedValueSet.Manual[0] = this.ConvertNumericValue(
                                        elementDefinition,
                                        parameterShortName,
                                        true,
                                        subscriber,
                                        modifiedValueSet.Manual[0],
                                        oldScale,
                                        newScale,
                                        conversionFactor,
                                        ref errorCount);
                                }

                                if (modifiedValueSet.Reference.Count == 1 && errorCount == 0)
                                {
                                    modifiedValueSet.Reference[0] = this.ConvertNumericValue(
                                        elementDefinition,
                                        parameterShortName,
                                        true,
                                        subscriber,
                                        modifiedValueSet.Reference[0],
                                        oldScale,
                                        newScale,
                                        conversionFactor,
                                        ref errorCount);
                                }
                            }
                        }

                        if (errorCount == 0)
                        {
                            var modifiedParameter = unitOfWork.GetModifiableDomainObject(parameter);
                            modifiedParameter.Scale = newScale;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Convert a numeric value from old scale to new scale.
        /// </summary>
        /// <param name="elementDefinition">
        /// The element definition.
        /// </param>
        /// <param name="parameterShortName">
        /// The parameter short name.
        /// </param>
        /// <param name="isSubscription">
        /// Assertion whether this is a parameter subscription or not.
        /// </param>
        /// <param name="owner">
        /// The owner short name.
        /// </param>
        /// <param name="oldValue">
        /// The old value. If the old value is a blank string or null, it is reset to the OCDT default value "-".
        /// </param>
        /// <param name="oldScale">
        /// The old scale.
        /// </param>
        /// <param name="newScale">
        /// The new scale.
        /// </param>
        /// <param name="conversionFactor">
        /// The conversion factor.
        /// </param>
        /// <param name="errorCount">
        /// Error count, that is incremented for every conversion error detected.
        /// </param>
        /// <returns>
        /// The string value converted to the new scale.
        /// </returns>
        private string ConvertNumericValue(
            ElementDefinition elementDefinition,
            string parameterShortName,
            bool isSubscription,
            string owner,
            string oldValue,
            MeasurementScale oldScale,
            MeasurementScale newScale,
            double conversionFactor,
            ref int errorCount)
        {
            if (string.IsNullOrWhiteSpace(oldValue))
            {
                oldValue = "-";
            }

            var newValue = oldValue;

            if (oldValue != "-")
            {
                var ownership = isSubscription ? "subscribed by" : "owned by";

                var messageStart = string.Format(
                    "In {0} parameter \"{1}\" ({2} {3}) value {4} {5}",
                    elementDefinition.ShortName,
                    parameterShortName,
                    ownership,
                    owner,
                    oldValue,
                    oldScale.ShortName);

                try
                {
                    newValue = (Convert.ToDouble(oldValue) * conversionFactor).ToString(CultureInfo.InvariantCulture);
                    Logger.Debug("{0} is converted to {1} {2}", messageStart, newValue, newScale.ShortName);
                }
                catch (FormatException ex)
                {
                    Logger.Error("{0} cannot be converted", messageStart);
                    errorCount++;
                }
            }

            return newValue;
        }

        /// <summary>
        /// Ungroup all nested parameter groups and ungroup parameters from given parameter groups.
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        /// <param name="parameterGroupNamesToUngroup">
        /// The parameter group names to ungroup.
        /// </param>
        private void UngroupParameterGroups(Iteration iteration, string[] parameterGroupNamesToUngroup)
        {
            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                // Flatten ParameterGroups to be all at level one under their ElementDefinition
                foreach (var parameterGroup in elementDefinition.ParameterGroup)
                {
                    if (parameterGroup.ContainingGroup != null)
                    {
                        var mod = unitOfWork.GetModifiableDomainObject(parameterGroup);
                        mod.ContainingGroup = null;
                    }
                }

                // Ungroup parameters that are in one of the parameter groups to ungroup
                foreach (var parameter in elementDefinition.Parameter)
                {
                    if (parameter.Group == null)
                    {
                        continue;
                    }

                    var parameterGroupName = parameter.Group.Name;

                    if (Enumerable.Contains(parameterGroupNamesToUngroup, parameterGroupName))
                    {
                        var mod = unitOfWork.GetModifiableDomainObject(parameter);
                        mod.Group = null;
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Delete parameter groups for given parameter group names.
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        /// <param name="parameterGroupNamesToDelete">
        /// The parameter group names to delete.
        /// </param>
        private void DeleteParameterGroups(Iteration iteration, string[] parameterGroupNamesToDelete)
        {
            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                foreach (var parameterGroup in elementDefinition.ParameterGroup)
                {
                    var parameterGroupName = parameterGroup.Name;

                    if (Enumerable.Contains(parameterGroupNamesToDelete, parameterGroupName))
                    {
                        var mod = unitOfWork.GetModifiableDomainObject(elementDefinition);
                        mod.ParameterGroup.RemoveWhere(pg => pg.Name == parameterGroupName);
                        Logger.Info("Deleted parameter group {0} from ED {1}", parameterGroupName, elementDefinition);
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Rename parameter groups according to given map
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        /// <param name="groupRenameMap">
        /// The group rename map, where key is the old name and value is the new name
        /// </param>
        private void RenameParameterGroups(Iteration iteration, Dictionary<string, string> groupRenameMap)
        {
            var unitOfWork = new UnitOfWork();

            // Create a reverse map for testing purposes
            var reverseNamesMap = new Dictionary<string, string>();

            foreach (var pair in groupRenameMap)
            {
                reverseNamesMap[pair.Value] = pair.Key;
            }

            var actualNamesMap = groupRenameMap;

            foreach (var elementDefinition in iteration.Element)
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                foreach (var parameterGroup in elementDefinition.ParameterGroup)
                {
                    ReplaceNames(parameterGroup, actualNamesMap, unitOfWork);

                    // Replace space with underscore if applicable
                    var modParameterGroup = unitOfWork.GetModifiableDomainObject(parameterGroup);
                    modParameterGroup.Name = modParameterGroup.Name.Replace(" ", "_");
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Replace old name with new name on given <see cref="Thing" />. The old name is matched insensitive of case.
        /// </summary>
        /// <param name="thing">
        /// The <see cref="Thing" />.
        /// </param>
        /// <param name="namesMap">
        /// The old versus new names map. Dictionary where key is old name, value is new name.
        /// </param>
        /// <param name="unitOfWork">
        /// The active <see cref="UnitOfWork" />.
        /// </param>
        private void ReplaceNames(Thing thing, Dictionary<string, string> namesMap, UnitOfWork unitOfWork)
        {
            if (thing == null)
            {
                return;
            }

            var namedThing = thing as INamedThing;

            if (namedThing == null)
            {
                return;
            }

            foreach (var pair in namesMap)
            {
                var oldName = pair.Key.ToLowerInvariant();
                var newName = pair.Value;
                var name = namedThing.Name.ToLowerInvariant().Trim();

                if (name == oldName)
                {
                    dynamic mod = unitOfWork.GetModifiableDomainObject(thing);
                    mod.Name = newName;

                    Logger.Info(
                        "Replaced {0}.Name \"{1}\" with \"{2}\"",
                        thing.GetType().Name,
                        namedThing.Name,
                        mod.Name);
                }
            }
        }

        /// <summary>
        /// Report on the given <see cref="Iteration" />.
        /// </summary>
        /// <param name="iteration">
        /// The <see cref="Iteration" /> to report.
        /// </param>
        private void ReportIteration(Iteration iteration)
        {
            var engineeringModelSetup = iteration.ContainerEngineeringModel.EngineeringModelSetup;
            Logger.Info("Report on model {0} ({1})", engineeringModelSetup.Name, engineeringModelSetup.ShortName);

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                var groups = string.Join(
                    ", ",
                    Enumerable.Select(Enumerable.OrderBy(elementDefinition.ParameterGroup, x => x.Name), x => x.Name));

                Logger.Info(
                    "ElementDefinition {0} ({1}) has ParameterGroups: {2}",
                    elementDefinition.ShortName,
                    elementDefinition.Name,
                    groups);
            }

            var allGroups = ObjStore.GetAllPersistentDomainObjects().OfType<ParameterGroup>().ToList();
            var groupNames = allGroups.Select(x => x.Name).Distinct().OrderBy(x => x);

            Logger.Info(
                "Overview of ParameterGroups in {0} ({1}):\n{2}",
                engineeringModelSetup.Name,
                engineeringModelSetup.ShortName,
                string.Join("\n", groupNames));
        }

        /// <summary>
        /// Report all parameters on the given <see cref="Iteration" />.
        /// </summary>
        /// <param name="iteration">
        /// The <see cref="Iteration" /> to report.
        /// </param>
        private void ReportParameters(Iteration iteration)
        {
            var engineeringModelSetup = iteration.ContainerEngineeringModel.EngineeringModelSetup;
            var engineeringModelShortName = engineeringModelSetup.ShortName;
            var csvPath = engineeringModelShortName + "_parameters_report.csv";

            try
            {
                var csvFileWriter = new CsvFileWriter(csvPath);

                var headings = new List<string>
                {
                    "Model.SN", "ED.Name", "ED.SN", "Owner", "Subscriber", "Categories", "Group", "Param.Type.RDL",
                    "Param.Name", "Param.SN",
                    "State.SN", "Option.SN", "Actual", "Published", "Scale", "Switch", "Computed", "Manual",
                    "Reference", "Formula"
                };

                csvFileWriter.WriteRow(headings);

                foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
                {
                    this.WriteElementDefinition(csvFileWriter, headings, engineeringModelShortName, elementDefinition);

                    foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                    foreach (var parameterValueSet in parameter.ValueSet)
                    {
                        this.WriteParameterRow(csvFileWriter, headings, engineeringModelShortName, parameterValueSet);

                        foreach (var parameterSubscription in Enumerable.OrderBy(
                            parameter.ParameterSubscription,
                            x =>
                                x.Owner.ShortName))
                        foreach (var parameterSubscriptionValueSet in parameterSubscription.ValueSet)
                        {
                            this.WriteParameterRow(
                                csvFileWriter,
                                headings,
                                engineeringModelShortName,
                                parameterSubscriptionValueSet);
                        }
                    }
                }

                csvFileWriter.Close();
            }
            catch (IOException ex)
            {
                Logger.Error(ex.Message);
            }
        }

        /// <summary>
        /// Write element definition.
        /// </summary>
        /// <param name="csvFileWriter">
        /// The CSV file writer.
        /// </param>
        /// <param name="headings">
        /// The headings.
        /// </param>
        /// <param name="engineeringModelShortName">
        /// The engineering model short name.
        /// </param>
        /// <param name="elementDefinition">
        /// The element definition.
        /// </param>
        private void WriteElementDefinition(
            CsvFileWriter csvFileWriter,
            List<string> headings,
            string engineeringModelShortName,
            ElementDefinition elementDefinition)
        {
            var fields = new string[headings.Count];
            fields[headings.IndexOf("Model.SN")] = engineeringModelShortName;
            fields[headings.IndexOf("ED.Name")] = elementDefinition.Name;
            fields[headings.IndexOf("ED.SN")] = elementDefinition.ShortName;
            fields[headings.IndexOf("Owner")] = elementDefinition.Owner.ShortName;
            fields[headings.IndexOf("Subscriber")] = "";

            fields[headings.IndexOf("Categories")] = string.Join(
                ", ",
                Enumerable.OrderBy(Enumerable.Select(elementDefinition.Category, cat => cat.ShortName), sn => sn));

            fields[headings.IndexOf("Group")] = "";
            fields[headings.IndexOf("Param.Type.RDL")] = "";
            fields[headings.IndexOf("Param.Name")] = "";
            fields[headings.IndexOf("Param.SN")] = "";
            fields[headings.IndexOf("State.SN")] = "";
            fields[headings.IndexOf("Option.SN")] = "";
            fields[headings.IndexOf("Actual")] = "";
            fields[headings.IndexOf("Published")] = "";
            fields[headings.IndexOf("Scale")] = "";
            fields[headings.IndexOf("Switch")] = "";
            fields[headings.IndexOf("Computed")] = "";
            fields[headings.IndexOf("Manual")] = "";
            fields[headings.IndexOf("Reference")] = "";
            fields[headings.IndexOf("Formula")] = "";
            csvFileWriter.WriteRow(fields);
        }

        /// <summary>
        /// Write parameter row.
        /// </summary>
        /// <param name="csvFileWriter">
        /// The CSV file writer.
        /// </param>
        /// <param name="headings">
        /// The headings.
        /// </param>
        /// <param name="engineeringModelShortName">
        /// The engineering model short name.
        /// </param>
        /// <param name="parameterValueSet">
        /// The parameter value set.
        /// </param>
        private void WriteParameterRow(
            CsvFileWriter csvFileWriter,
            List<string> headings,
            string engineeringModelShortName,
            ParameterValueSet parameterValueSet)
        {
            var elementDefinition = parameterValueSet.ContainerParameter.ContainerElementDefinition;
            var fields = new string[headings.Count];
            fields[headings.IndexOf("Model.SN")] = engineeringModelShortName;
            fields[headings.IndexOf("ED.Name")] = elementDefinition.Name;
            fields[headings.IndexOf("ED.SN")] = elementDefinition.ShortName;
            fields[headings.IndexOf("Owner")] = parameterValueSet.Owner.ShortName;
            fields[headings.IndexOf("Subscriber")] = "";

            fields[headings.IndexOf("Categories")] = string.Join(
                ", ",
                elementDefinition.Category.Select(cat => cat.ShortName).OrderBy(sn => sn));

            fields[headings.IndexOf("Group")] = parameterValueSet.ContainerParameter.GetParameterGroupPath();

            fields[headings.IndexOf("Param.Type.RDL")] = parameterValueSet.ContainerParameter.ParameterType
                .ContainingReferenceDataLibrary.ShortName;

            fields[headings.IndexOf("Param.Name")] = parameterValueSet.ContainerParameter.ParameterType.Name;
            fields[headings.IndexOf("Param.SN")] = parameterValueSet.ContainerParameter.ParameterType.ShortName;

            fields[headings.IndexOf("State.SN")] = parameterValueSet.ActualState == null
                ? ""
                : Utils.ToStringNullSafe(parameterValueSet.ActualState.ShortName);

            fields[headings.IndexOf("Option.SN")] = parameterValueSet.ActualOption == null
                ? ""
                : Utils.ToStringNullSafe(parameterValueSet.ActualOption.ShortName);

            fields[headings.IndexOf("Actual")] = parameterValueSet.ActualValue == null
                ? ""
                : Utils.ToStringNullSafe(Enumerable.FirstOrDefault(parameterValueSet.ActualValue));

            fields[headings.IndexOf("Published")] = parameterValueSet.Published == null
                ? ""
                : Utils.ToStringNullSafe(Enumerable.FirstOrDefault(parameterValueSet.Published));

            fields[headings.IndexOf("Scale")] = parameterValueSet.ContainerParameter.Scale == null
                ? ""
                : parameterValueSet.ContainerParameter.Scale.ShortName;

            fields[headings.IndexOf("Switch")] = parameterValueSet.ValueSwitch.ToString();

            fields[headings.IndexOf("Computed")] =
                string.Join((string)"|", (IEnumerable<string>)parameterValueSet.Computed.Select(Utils.ToStringNullSafe));

            fields[headings.IndexOf("Manual")] =
                string.Join((string)"|", (IEnumerable<string>)parameterValueSet.Manual.Select(Utils.ToStringNullSafe));

            fields[headings.IndexOf("Reference")] =
                string.Join((string)"|", (IEnumerable<string>)parameterValueSet.Reference.Select(Utils.ToStringNullSafe));

            fields[headings.IndexOf("Formula")] = string.Join(
                "|",
                Enumerable.Select(parameterValueSet.Formula, s => $"\"{Utils.ToStringNullSafe(s)}\""));

            csvFileWriter.WriteRow(fields);
        }

        /// <summary>
        /// Write parameter row.
        /// </summary>
        /// <param name="csvFileWriter">
        /// The CSV file writer.
        /// </param>
        /// <param name="headings">
        /// The headings.
        /// </param>
        /// <param name="engineeringModelShortName">
        /// The engineering model short name.
        /// </param>
        /// <param name="subscriptionValueSet">
        /// The subscription value set.
        /// </param>
        private void WriteParameterRow(
            CsvFileWriter csvFileWriter,
            List<string> headings,
            string engineeringModelShortName,
            ParameterSubscriptionValueSet subscriptionValueSet)
        {
            var fields = new string[headings.Count];
            var elementDefinition = subscriptionValueSet.ContainerParameterSubscription.GetContainerElementDefinition();

            var parameterOrOverrideBase =
                subscriptionValueSet.ContainerParameterSubscription.ContainerParameterOrOverrideBase;

            fields[headings.IndexOf("Model.SN")] = engineeringModelShortName;
            fields[headings.IndexOf("ED.Name")] = elementDefinition.Name;
            fields[headings.IndexOf("ED.SN")] = elementDefinition.ShortName;
            fields[headings.IndexOf("Owner")] = parameterOrOverrideBase.Owner.ShortName;
            fields[headings.IndexOf("Subscriber")] = subscriptionValueSet.Owner.ShortName;

            fields[headings.IndexOf("Categories")] = string.Join(
                ", ",
                elementDefinition.Category.Select(cat => cat.ShortName).OrderBy(sn => sn));

            fields[headings.IndexOf("Group")] =
                subscriptionValueSet.SubscribedValueSet.DeriveParameter().GetParameterGroupPath();

            fields[headings.IndexOf("Param.Type.RDL")] =
                parameterOrOverrideBase.ParameterType.ContainingReferenceDataLibrary.ShortName;

            fields[headings.IndexOf("Param.Name")] = parameterOrOverrideBase.ParameterType.Name;
            fields[headings.IndexOf("Param.SN")] = parameterOrOverrideBase.ParameterType.ShortName;

            fields[headings.IndexOf("State.SN")] = subscriptionValueSet.ActualState == null
                ? ""
                : subscriptionValueSet.ActualState.ShortName;

            fields[headings.IndexOf("Option.SN")] = subscriptionValueSet.ActualOption == null
                ? ""
                : subscriptionValueSet.ActualOption.ShortName;

            fields[headings.IndexOf("Actual")] = Enumerable.FirstOrDefault(subscriptionValueSet.ActualValue);
            fields[headings.IndexOf("Published")] = "";

            fields[headings.IndexOf("Scale")] =
                parameterOrOverrideBase.Scale == null ? "" : parameterOrOverrideBase.Scale.ShortName;

            fields[headings.IndexOf("Switch")] = subscriptionValueSet.ValueSwitch.ToString();
            fields[headings.IndexOf("Computed")] = string.Join("|", subscriptionValueSet.Computed);
            fields[headings.IndexOf("Manual")] = string.Join("|", subscriptionValueSet.Manual);
            fields[headings.IndexOf("Reference")] = string.Join("|", subscriptionValueSet.Reference);
            fields[headings.IndexOf("Formula")] = "";
            csvFileWriter.WriteRow(fields);
        }

        /// <summary>
        /// Apply option dependence to selected parameters and overrides for element definitions and usages of selected categories.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="selectedParameters">
        /// List of short names of the selected parameters and parameter overrides.
        /// </param>
        private void ApplyOptionDependenceToParameters(Iteration iteration, List<string> selectedParameters)
        {
            if (selectedParameters.Count == 0)
            {
                Logger.Warn("No --parameters given. Apply option dependence skipped.");
                return;
            }

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                // Apply option dependence to the selected parameters
                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                {
                    if (selectedParameters.Contains(parameter.ParameterType.ShortName))
                    {
                        if (!parameter.IsOptionDependent)
                        {
                            var modifiableParameter = unitOfWork.GetModifiableDomainObject(parameter);
                            modifiableParameter.IsOptionDependent = true;
                            Logger.Info($"Parameter {parameter.Path} made option dependent");
                        }
                        else
                        {
                            Logger.Info($"Parameter {parameter.Path} was already option dependent");
                        }
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Apply option dependence to selected parameters and overrides for element definitions and usages of selected categories.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        private void MoveRefToManualValues(Iteration iteration)
        {
            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                {
                    if (parameter.ParameterType is ScalarParameterType)
                    {
                        var valueSet = Enumerable.FirstOrDefault(parameter.ValueSet);

                        if (valueSet != null)
                        {
                            var refValue = valueSet.Reference;
                            var manualValue = valueSet.Manual;

                            if (manualValue[0] == "-" && valueSet.ValueSwitch == ParameterSwitchKind.REFERENCE)
                            {
                                var modValueSet = unitOfWork.GetModifiableDomainObject(valueSet);
                                modValueSet.Manual[0] = refValue[0];
                                modValueSet.ValueSwitch = ParameterSwitchKind.MANUAL;
                                modValueSet.Reference[0] = "-";
                                Logger.Info($"Moved {parameter.Path} = {refValue[0]} ref value to manual value and changed switch to MANUAL");
                            }
                        }
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Remove option dependence from selected parameters and overrides for element definitions and usages of selected
        /// categories.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="selectedParameters">
        /// List of short names of the selected parameters and parameter overrides.
        /// </param>
        private void RemoveOptionDependenceFromParameters(Iteration iteration, List<string> selectedParameters)
        {
            if (selectedParameters.Count == 0)
            {
                Logger.Warn("No --parameters given. Remove option dependence skipped.");
                return;
            }

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                // Remove option dependence from the selected parameters
                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                {
                    if (selectedParameters.Contains(parameter.ParameterType.ShortName))
                    {
                        if (parameter.IsOptionDependent)
                        {
                            var modifiableParameter = unitOfWork.GetModifiableDomainObject(parameter);
                            modifiableParameter.IsOptionDependent = false;
                            Logger.Info($"Parameter {parameter.Path} made NOT option dependent");
                        }
                        else
                        {
                            Logger.Info($"Parameter {parameter.Path} was already NOT option dependent");
                        }
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Apply given actual finite state list as state dependence to selected parameters and overrides.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="actualFiniteStateListShortName">
        /// Short name of the actual finite state list to apply.
        /// </param>
        /// <param name="selectedParameters">
        /// List of short names of the selected parameters and parameter overrides.
        /// </param>
        private void ApplyStateDependenceToParameters(
            Iteration iteration,
            string actualFiniteStateListShortName,
            List<string> selectedParameters)
        {
            if (selectedParameters.Count == 0)
            {
                Logger.Warn("No --parameters given. Apply state dependence skipped.");
                return;
            }

            var actualFiniteStateList =
                Enumerable.SingleOrDefault(iteration.ActualFiniteStateList, x => x.ShortName == actualFiniteStateListShortName);

            if (actualFiniteStateList == null)
            {
                Logger.Warn(
                    "Cannot find Actual Finite State List \"{0}\". Apply state dependence skipped.",
                    actualFiniteStateListShortName);

                return;
            }

            Logger.Info("Applying state dependency \"{0}\"", actualFiniteStateListShortName);

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                // Visit all parameters in the element definitions and make the selected ones dependent on the given state if not already
                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                {
                    // Filter for selected Parameters
                    if (!selectedParameters.Contains(parameter.ParameterType.ShortName))
                    {
                        continue;
                    }

                    if (parameter.StateDependence == null || parameter.StateDependence.Iid != actualFiniteStateList.Iid)
                    {
                        var modifiableParameter = unitOfWork.GetModifiableDomainObject(parameter);
                        modifiableParameter.StateDependence = actualFiniteStateList;

                        if (parameter.StateDependence == null)
                        {
                            Logger.Info($"State {actualFiniteStateListShortName} applied to Parameter {parameter.Path}");
                        }
                        else
                        {
                            Logger.Warn($"State {actualFiniteStateListShortName} applied to Parameter {parameter.Path}, changed from {parameter.StateDependence.ShortName}");
                        }
                    }
                    else
                    {
                        Logger.Info($"State {actualFiniteStateListShortName} was already applied to Parameter {parameter.Path}");
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Remove given actual finite state list as state dependence from selected parameters and overrides.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="actualFiniteStateListShortName">
        /// Short name of the actual finite state list to remove.
        /// </param>
        /// <param name="selectedParameters">
        /// List of short names of the selected parameters and parameter overrides.
        /// </param>
        private void RemoveStateDependenceFromParameters(
            Iteration iteration,
            string actualFiniteStateListShortName,
            List<string> selectedParameters)
        {
            if (selectedParameters.Count == 0)
            {
                Logger.Warn("No --parameters given. Remove state dependence skipped.");
                return;
            }

            var actualFiniteStateList =
                Enumerable.SingleOrDefault(iteration.ActualFiniteStateList, x => x.ShortName == actualFiniteStateListShortName);

            if (actualFiniteStateList == null)
            {
                Logger.Warn($"Cannot find Actual Finite State List \"{actualFiniteStateListShortName}\". Remove parameters state dependent skipped.");
                return;
            }

            Logger.Info($"Removing state dependency \"{actualFiniteStateListShortName}\"");

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                // Visit all parameters in the element definitions and remove the given state from the selected parameters
                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                {
                    if (selectedParameters.Contains(parameter.ParameterType.ShortName))
                    {
                        if (parameter.StateDependence != null &&
                            parameter.StateDependence.Iid == actualFiniteStateList.Iid)
                        {
                            var modifiableParameter = unitOfWork.GetModifiableDomainObject(parameter);
                            modifiableParameter.StateDependence = null;
                            Logger.Warn($"State {actualFiniteStateListShortName} removed from Parameter {parameter.Path}");
                        }
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Adds <see cref="Parameter" />s to all <see cref="ElementDefinition" />s of the given <see cref="Iteration" />.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="selectedParameters">
        /// List of short names of the parameters to add.
        /// </param>
        /// <param name="parameterGroupName">
        /// Name of <see cref="ParameterGroup" /> to use.
        /// If specified group will be created if it did not exist, and parameter will be added into the group.
        /// If the name is null or empty the parameters will be added directly under the element definitions.
        /// </param>
        /// <param name="domain">
        /// Short name of owner <see cref="DomainOfExpertise" /> to use. If not specified the owner of each element definition will
        /// be used.
        /// </param>
        private void AddParameters(
            Iteration iteration,
            List<string> selectedParameters,
            string parameterGroupName,
            string domain)
        {
            if (selectedParameters.Count == 0)
            {
                Logger.Warn("Action add-parameters: No --parameters given.");
                return;
            }

            var aggregateRdl = iteration.AggregateReferenceDataLibrary;
            var selectedParameterTypes = new List<ParameterType>();

            foreach (var selectedParameter in selectedParameters)
            {
                var parameterType = aggregateRdl.ParameterType.SingleOrDefault(pt => pt.ShortName == selectedParameter);

                if (parameterType == null)
                {
                    Logger.Warn($"Action add-parameters: parameter type with short name \"{selectedParameter}\" not found.");
                }
                else
                {
                    selectedParameterTypes.Add(parameterType);
                }
            }

            if (selectedParameterTypes.Count == 0)
            {
                return;
            }

            DomainOfExpertise owner = null;

            if (!string.IsNullOrEmpty(domain))
            {
                owner = this.SiteDirectory.Domain.FirstOrDefault(
                    domainOfExpertise =>
                        domainOfExpertise.ShortName == domain);

                if (owner == null)
                {
                    Logger.Warn(
                        "Action add-parameters: domain-of-expertise with short name \"{0}\" not found.",
                        domain);

                    return;
                }
            }

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                var modifiedElement = unitOfWork.GetModifiableDomainObject(elementDefinition);

                foreach (var parameterType in selectedParameterTypes)
                {
                    // Add parameter if it does not exist
                    var parameter = Enumerable.FirstOrDefault(elementDefinition.Parameter, p => p.ParameterType == parameterType);

                    if (parameter == null)
                    {
                        var parameterOwner = owner ?? elementDefinition.Owner;
                        var quantityKind = parameterType as QuantityKind;
                        var defaultScale = quantityKind == null ? null : quantityKind.DefaultScale;

                        modifiedElement.Parameter.Add(
                            unitOfWork.CreateParameter(
                                owner: parameterOwner,
                                parameterType: parameterType,
                                scale: defaultScale));

                        Logger.Info($"In {elementDefinition.ShortName} added Parameter {parameterType.ShortName}");
                    }

                    // If ParameterGroup specified then move Parameter to that group
                    if (!string.IsNullOrEmpty(parameterGroupName))
                    {
                        var parameterGroup = this.GetOrCreateParameterGroup(modifiedElement, parameterGroupName, unitOfWork);
                        this.MoveParameterToGroup(modifiedElement, parameterType.ShortName, parameterGroup, unitOfWork);
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Remove <see cref="Parameter" />s from all <see cref="ElementDefinition" />s of the given <see cref="Iteration" />.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="selectedParameters">
        /// List of short names of the parameters to remove.
        /// </param>
        /// <param name="domain">
        /// Short name of owner <see cref="DomainOfExpertise" /> to use. If not specified the owner of each element definition will
        /// be used.
        /// </param>
        private void RemoveParameters(
            Iteration iteration,
            List<string> selectedParameters,
            string domain)
        {
            if (selectedParameters.Count == 0)
            {
                Logger.Error("Action remove-parameters: No --parameters given.");
                return;
            }

            var aggregateRdl = iteration.AggregateReferenceDataLibrary;
            var selectedParameterTypes = new List<ParameterType>();

            foreach (var selectedParameter in selectedParameters)
            {
                var parameterType = aggregateRdl.ParameterType.SingleOrDefault(pt => pt.ShortName == selectedParameter);

                if (parameterType == null)
                {
                    Logger.Warn($"Action remove-parameters: parameter type with short name \"{selectedParameter}\" not found.");
                }
                else
                {
                    selectedParameterTypes.Add(parameterType);
                }
            }

            if (selectedParameterTypes.Count == 0)
            {
                return;
            }

            DomainOfExpertise owner = null;

            if (!string.IsNullOrEmpty(domain))
            {
                owner = this.SiteDirectory.Domain.FirstOrDefault(
                    domainOfExpertise =>
                        domainOfExpertise.ShortName == domain);

                if (owner == null)
                {
                    Logger.Warn($"Action remove-parameters: domain-of-expertise with short name \"{domain}\" not found.");
                    return;
                }
            }

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                var modifiedElement = unitOfWork.GetModifiableDomainObject(elementDefinition);

                foreach (var parameterType in selectedParameterTypes)
                {
                    // Remove parameter if it exists and if owner is given, owned by given domain
                    var parameter = Enumerable.FirstOrDefault(elementDefinition.Parameter, p => p.ParameterType == parameterType);

                    if (parameter != null)
                    {
                        if (owner == null || parameter.Owner == owner)
                        {
                            modifiedElement.Parameter.Remove(parameter);
                            Logger.Info($"In {elementDefinition.ShortName} removed Parameter {parameterType.ShortName}");
                        }
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Subscribe parameters and parameter overrides with given short names for given subscriber.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="subscriber">
        /// Subscriber <see cref="DomainOfExpertise" /> to use.
        /// </param>
        /// <param name="selectedParameters">
        /// List of short names of the parameters or parameter overrides to subscribe to.
        /// </param>
        private void SubscribeParameters(
            Iteration iteration,
            DomainOfExpertise subscriber,
            List<string> selectedParameters)
        {
            if (subscriber == null)
            {
                Logger.Warn(
                    "Unknown subscriber domain of expertise: \"{0}\". Subscribe parameters skipped.",
                    this.options.DomainOfExpertise);

                return;
            }

            if (selectedParameters.Count == 0)
            {
                Logger.Warn("No --parameters given. Subscribe parameters skipped.");
                return;
            }

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                // Visit all parameters in the element definitions and take a subscription if requested and not taken already
                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))

                    // Take subscription on this parameter if no subscription taken already
                {
                    if (selectedParameters.Contains(parameter.ParameterType.ShortName) && parameter.Owner.Iid !=
                                                                                       subscriber.Iid
                                                                                       && parameter
                                                                                           .ParameterSubscription
                                                                                           .None(
                                                                                               parameterSubscription =>
                                                                                                   parameterSubscription
                                                                                                       .Owner ==
                                                                                                   subscriber))
                    {
                        var parameterSubscription =
                            unitOfWork.CreateParameterSubscription(owner: subscriber, container: parameter);

                        Logger.Info($"Parameter Subscription taken on {parameterSubscription.Path}");
                    }
                }

                // Visit all parameter overrides in the element usages and take a subscription if requested and not taken already
                foreach (var elementUsage in Enumerable.OrderBy(elementDefinition.ContainedElement, x => x.ShortName))
                foreach (var parameterOverride in Enumerable.OrderBy(elementUsage.ParameterOverride, x => x.ParameterType.ShortName)
                    )

                    // Take subscription on this parameter override if no subscription is taken already
                {
                    if (selectedParameters.Contains(parameterOverride.ParameterType.ShortName) && parameterOverride
                                                                                                   .Owner.Iid !=
                                                                                               subscriber.Iid
                                                                                               && parameterOverride
                                                                                                   .ParameterSubscription
                                                                                                   .None(
                                                                                                       parameterSubscription =>
                                                                                                           parameterSubscription
                                                                                                               .Owner ==
                                                                                                           subscriber))
                    {
                        var parameterSubscription =
                            unitOfWork.CreateParameterSubscription(owner: subscriber, container: parameterOverride);

                        Logger.Info($"Parameter Override Subscription taken on {parameterSubscription.Path}");
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Set the switch on all value sets of the <see cref="ParameterSubscription" />s of the given subscriber to the given
        /// switch value.
        /// </summary>
        /// <param name="iteration">
        /// Engineering model iteration to use.
        /// </param>
        /// <param name="subscriber">
        /// The subscriber <see cref="DomainOfExpertise" />.
        /// </param>
        /// <param name="parameterSwitchKind">
        /// The <see cref="ParameterSwitchKind" /> to set.
        /// </param>
        private void SetParameterSubscriptionsSwitch(
            Iteration iteration,
            DomainOfExpertise subscriber,
            ParameterSwitchKind parameterSwitchKind)
        {
            if (subscriber == null)
            {
                Logger.Warn($"Unknown subscriber domain of expertise: \"{this.options.DomainOfExpertise}\". Subscribe parameters skipped.");
                return;
            }

            var unitOfWork = new UnitOfWork();
            var changeCount = 0;

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                // Visit all parameters in the element definitions and take a subscription if requested and not taken already
                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                foreach (var parameterSubscription in parameter.ParameterSubscription)
                {
                    if (parameterSubscription.Owner != subscriber)
                    {
                        continue;
                    }

                    foreach (var parameterSubscriptionValueSet in parameterSubscription.ValueSet)
                    {
                        var mod = unitOfWork.GetModifiableDomainObject(parameterSubscriptionValueSet);
                        mod.ValueSwitch = parameterSwitchKind;
                        changeCount++;
                    }
                }

                // Visit all parameter overrides in the element usages and take a subscription if requested and not taken already
                foreach (var elementUsage in Enumerable.OrderBy(elementDefinition.ContainedElement, x => x.ShortName))
                foreach (var parameterOverride in Enumerable.OrderBy(elementUsage.ParameterOverride, x => x.ParameterType.ShortName)
                )
                foreach (var parameterSubscription in parameterOverride.ParameterSubscription)
                {
                    if (parameterSubscription.Owner != subscriber)
                    {
                        continue;
                    }

                    foreach (var parameterSubscriptionValueSet in parameterSubscription.ValueSet)
                    {
                        var mod = unitOfWork.GetModifiableDomainObject(parameterSubscriptionValueSet);
                        mod.ValueSwitch = parameterSwitchKind;
                    }

                    changeCount++;
                }
            }

            Logger.Info($"Set switch to {parameterSwitchKind} on {changeCount} Parameter or Parameter Override Subscriptions for {subscriber.ShortName}");

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }

        /// <summary>
        /// Assign measurement scale to given parameters.
        /// </summary>
        /// <param name="iteration">
        /// The iteration.
        /// </param>
        /// <param name="selectedParameters">
        /// The selected parameters.
        /// </param>
        /// <param name="measurementScale">
        /// The measurement scale.
        /// </param>
        private void AssignMeasurementScale(
            Iteration iteration,
            List<string> selectedParameters,
            MeasurementScale measurementScale)
        {
            if (selectedParameters.Count == 0)
            {
                Logger.Warn("No --parameters given. Action set-scale skipped.");
                return;
            }

            var unitOfWork = new UnitOfWork();

            foreach (var elementDefinition in Enumerable.OrderBy(iteration.Element, x => x.ShortName))
            {
                if (this.IsFilteredOut(elementDefinition))
                {
                    continue;
                }

                foreach (var parameter in Enumerable.OrderBy(elementDefinition.Parameter, x => x.ParameterType.ShortName))
                {
                    var quantityKind = parameter.ParameterType as QuantityKind;

                    if (quantityKind == null)
                    {
                        continue;
                    }

                    var allPossibleScale = quantityKind.AllPossibleScale;

                    if (parameter.Scale == null)
                    {
                        Logger.Warn(
                            "No measurement scale assigned to parameter {0}: should be one of {1}",
                            parameter.Path,
                            string.Join(", ", allPossibleScale.Select(x => x.ShortName).OrderBy(x => x)));
                    }
                    else if (!allPossibleScale.Contains(parameter.Scale))
                    {
                        Logger.Warn(
                            "Invalid measurement scale {0} assigned to parameter {1}: should be one of {2}",
                            parameter.Scale.ShortName,
                            parameter.Path,
                            string.Join(", ", allPossibleScale.Select(x => x.ShortName).OrderBy(x => x)));
                    }

                    if (selectedParameters.Contains(parameter.ParameterType.ShortName))
                    {
                        if (allPossibleScale.Contains(measurementScale) && parameter.Scale != measurementScale)
                        {
                            var mod = unitOfWork.GetModifiableDomainObject(parameter);
                            mod.Scale = measurementScale;

                            Logger.Info(
                                "Assigned scale \"{0}\" to parameter {1}",
                                measurementScale.ShortName,
                                parameter.Path);
                        }
                    }
                }
            }

            if (unitOfWork.HasChanges && !this.options.DryRun)
            {
                this.WebServiceClient.Write(unitOfWork);
            }

            unitOfWork.Dispose();
        }
    }
}
