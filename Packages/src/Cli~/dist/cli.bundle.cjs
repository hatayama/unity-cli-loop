#!/usr/bin/env node
"use strict";
var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __commonJS = (cb, mod) => function __require() {
  return mod || (0, cb[__getOwnPropNames(cb)[0]])((mod = { exports: {} }).exports, mod), mod.exports;
};
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
  // If the importer is in node compatibility mode or this is not an ESM
  // file that has been converted to a CommonJS file using a Babel-
  // compatible transform (i.e. "__esModule" has not been set), then set
  // "default" to the CommonJS "module.exports" for node compatibility.
  isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
  mod
));
var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

// node_modules/commander/lib/error.js
var require_error = __commonJS({
  "node_modules/commander/lib/error.js"(exports2) {
    var CommanderError2 = class extends Error {
      /**
       * Constructs the CommanderError class
       * @param {number} exitCode suggested exit code which could be used with process.exit
       * @param {string} code an id string representing the error
       * @param {string} message human-readable description of the error
       */
      constructor(exitCode, code, message) {
        super(message);
        Error.captureStackTrace(this, this.constructor);
        this.name = this.constructor.name;
        this.code = code;
        this.exitCode = exitCode;
        this.nestedError = void 0;
      }
    };
    var InvalidArgumentError2 = class extends CommanderError2 {
      /**
       * Constructs the InvalidArgumentError class
       * @param {string} [message] explanation of why argument is invalid
       */
      constructor(message) {
        super(1, "commander.invalidArgument", message);
        Error.captureStackTrace(this, this.constructor);
        this.name = this.constructor.name;
      }
    };
    exports2.CommanderError = CommanderError2;
    exports2.InvalidArgumentError = InvalidArgumentError2;
  }
});

// node_modules/commander/lib/argument.js
var require_argument = __commonJS({
  "node_modules/commander/lib/argument.js"(exports2) {
    var { InvalidArgumentError: InvalidArgumentError2 } = require_error();
    var Argument2 = class {
      /**
       * Initialize a new command argument with the given name and description.
       * The default is that the argument is required, and you can explicitly
       * indicate this with <> around the name. Put [] around the name for an optional argument.
       *
       * @param {string} name
       * @param {string} [description]
       */
      constructor(name, description) {
        this.description = description || "";
        this.variadic = false;
        this.parseArg = void 0;
        this.defaultValue = void 0;
        this.defaultValueDescription = void 0;
        this.argChoices = void 0;
        switch (name[0]) {
          case "<":
            this.required = true;
            this._name = name.slice(1, -1);
            break;
          case "[":
            this.required = false;
            this._name = name.slice(1, -1);
            break;
          default:
            this.required = true;
            this._name = name;
            break;
        }
        if (this._name.endsWith("...")) {
          this.variadic = true;
          this._name = this._name.slice(0, -3);
        }
      }
      /**
       * Return argument name.
       *
       * @return {string}
       */
      name() {
        return this._name;
      }
      /**
       * @package
       */
      _collectValue(value, previous) {
        if (previous === this.defaultValue || !Array.isArray(previous)) {
          return [value];
        }
        previous.push(value);
        return previous;
      }
      /**
       * Set the default value, and optionally supply the description to be displayed in the help.
       *
       * @param {*} value
       * @param {string} [description]
       * @return {Argument}
       */
      default(value, description) {
        this.defaultValue = value;
        this.defaultValueDescription = description;
        return this;
      }
      /**
       * Set the custom handler for processing CLI command arguments into argument values.
       *
       * @param {Function} [fn]
       * @return {Argument}
       */
      argParser(fn) {
        this.parseArg = fn;
        return this;
      }
      /**
       * Only allow argument value to be one of choices.
       *
       * @param {string[]} values
       * @return {Argument}
       */
      choices(values) {
        this.argChoices = values.slice();
        this.parseArg = (arg, previous) => {
          if (!this.argChoices.includes(arg)) {
            throw new InvalidArgumentError2(
              `Allowed choices are ${this.argChoices.join(", ")}.`
            );
          }
          if (this.variadic) {
            return this._collectValue(arg, previous);
          }
          return arg;
        };
        return this;
      }
      /**
       * Make argument required.
       *
       * @returns {Argument}
       */
      argRequired() {
        this.required = true;
        return this;
      }
      /**
       * Make argument optional.
       *
       * @returns {Argument}
       */
      argOptional() {
        this.required = false;
        return this;
      }
    };
    function humanReadableArgName(arg) {
      const nameOutput = arg.name() + (arg.variadic === true ? "..." : "");
      return arg.required ? "<" + nameOutput + ">" : "[" + nameOutput + "]";
    }
    exports2.Argument = Argument2;
    exports2.humanReadableArgName = humanReadableArgName;
  }
});

// node_modules/commander/lib/help.js
var require_help = __commonJS({
  "node_modules/commander/lib/help.js"(exports2) {
    var { humanReadableArgName } = require_argument();
    var Help2 = class {
      constructor() {
        this.helpWidth = void 0;
        this.minWidthToWrap = 40;
        this.sortSubcommands = false;
        this.sortOptions = false;
        this.showGlobalOptions = false;
      }
      /**
       * prepareContext is called by Commander after applying overrides from `Command.configureHelp()`
       * and just before calling `formatHelp()`.
       *
       * Commander just uses the helpWidth and the rest is provided for optional use by more complex subclasses.
       *
       * @param {{ error?: boolean, helpWidth?: number, outputHasColors?: boolean }} contextOptions
       */
      prepareContext(contextOptions) {
        this.helpWidth = this.helpWidth ?? contextOptions.helpWidth ?? 80;
      }
      /**
       * Get an array of the visible subcommands. Includes a placeholder for the implicit help command, if there is one.
       *
       * @param {Command} cmd
       * @returns {Command[]}
       */
      visibleCommands(cmd) {
        const visibleCommands = cmd.commands.filter((cmd2) => !cmd2._hidden);
        const helpCommand = cmd._getHelpCommand();
        if (helpCommand && !helpCommand._hidden) {
          visibleCommands.push(helpCommand);
        }
        if (this.sortSubcommands) {
          visibleCommands.sort((a, b) => {
            return a.name().localeCompare(b.name());
          });
        }
        return visibleCommands;
      }
      /**
       * Compare options for sort.
       *
       * @param {Option} a
       * @param {Option} b
       * @returns {number}
       */
      compareOptions(a, b) {
        const getSortKey = (option) => {
          return option.short ? option.short.replace(/^-/, "") : option.long.replace(/^--/, "");
        };
        return getSortKey(a).localeCompare(getSortKey(b));
      }
      /**
       * Get an array of the visible options. Includes a placeholder for the implicit help option, if there is one.
       *
       * @param {Command} cmd
       * @returns {Option[]}
       */
      visibleOptions(cmd) {
        const visibleOptions = cmd.options.filter((option) => !option.hidden);
        const helpOption = cmd._getHelpOption();
        if (helpOption && !helpOption.hidden) {
          const removeShort = helpOption.short && cmd._findOption(helpOption.short);
          const removeLong = helpOption.long && cmd._findOption(helpOption.long);
          if (!removeShort && !removeLong) {
            visibleOptions.push(helpOption);
          } else if (helpOption.long && !removeLong) {
            visibleOptions.push(
              cmd.createOption(helpOption.long, helpOption.description)
            );
          } else if (helpOption.short && !removeShort) {
            visibleOptions.push(
              cmd.createOption(helpOption.short, helpOption.description)
            );
          }
        }
        if (this.sortOptions) {
          visibleOptions.sort(this.compareOptions);
        }
        return visibleOptions;
      }
      /**
       * Get an array of the visible global options. (Not including help.)
       *
       * @param {Command} cmd
       * @returns {Option[]}
       */
      visibleGlobalOptions(cmd) {
        if (!this.showGlobalOptions) return [];
        const globalOptions = [];
        for (let ancestorCmd = cmd.parent; ancestorCmd; ancestorCmd = ancestorCmd.parent) {
          const visibleOptions = ancestorCmd.options.filter(
            (option) => !option.hidden
          );
          globalOptions.push(...visibleOptions);
        }
        if (this.sortOptions) {
          globalOptions.sort(this.compareOptions);
        }
        return globalOptions;
      }
      /**
       * Get an array of the arguments if any have a description.
       *
       * @param {Command} cmd
       * @returns {Argument[]}
       */
      visibleArguments(cmd) {
        if (cmd._argsDescription) {
          cmd.registeredArguments.forEach((argument) => {
            argument.description = argument.description || cmd._argsDescription[argument.name()] || "";
          });
        }
        if (cmd.registeredArguments.find((argument) => argument.description)) {
          return cmd.registeredArguments;
        }
        return [];
      }
      /**
       * Get the command term to show in the list of subcommands.
       *
       * @param {Command} cmd
       * @returns {string}
       */
      subcommandTerm(cmd) {
        const args = cmd.registeredArguments.map((arg) => humanReadableArgName(arg)).join(" ");
        return cmd._name + (cmd._aliases[0] ? "|" + cmd._aliases[0] : "") + (cmd.options.length ? " [options]" : "") + // simplistic check for non-help option
        (args ? " " + args : "");
      }
      /**
       * Get the option term to show in the list of options.
       *
       * @param {Option} option
       * @returns {string}
       */
      optionTerm(option) {
        return option.flags;
      }
      /**
       * Get the argument term to show in the list of arguments.
       *
       * @param {Argument} argument
       * @returns {string}
       */
      argumentTerm(argument) {
        return argument.name();
      }
      /**
       * Get the longest command term length.
       *
       * @param {Command} cmd
       * @param {Help} helper
       * @returns {number}
       */
      longestSubcommandTermLength(cmd, helper) {
        return helper.visibleCommands(cmd).reduce((max, command) => {
          return Math.max(
            max,
            this.displayWidth(
              helper.styleSubcommandTerm(helper.subcommandTerm(command))
            )
          );
        }, 0);
      }
      /**
       * Get the longest option term length.
       *
       * @param {Command} cmd
       * @param {Help} helper
       * @returns {number}
       */
      longestOptionTermLength(cmd, helper) {
        return helper.visibleOptions(cmd).reduce((max, option) => {
          return Math.max(
            max,
            this.displayWidth(helper.styleOptionTerm(helper.optionTerm(option)))
          );
        }, 0);
      }
      /**
       * Get the longest global option term length.
       *
       * @param {Command} cmd
       * @param {Help} helper
       * @returns {number}
       */
      longestGlobalOptionTermLength(cmd, helper) {
        return helper.visibleGlobalOptions(cmd).reduce((max, option) => {
          return Math.max(
            max,
            this.displayWidth(helper.styleOptionTerm(helper.optionTerm(option)))
          );
        }, 0);
      }
      /**
       * Get the longest argument term length.
       *
       * @param {Command} cmd
       * @param {Help} helper
       * @returns {number}
       */
      longestArgumentTermLength(cmd, helper) {
        return helper.visibleArguments(cmd).reduce((max, argument) => {
          return Math.max(
            max,
            this.displayWidth(
              helper.styleArgumentTerm(helper.argumentTerm(argument))
            )
          );
        }, 0);
      }
      /**
       * Get the command usage to be displayed at the top of the built-in help.
       *
       * @param {Command} cmd
       * @returns {string}
       */
      commandUsage(cmd) {
        let cmdName = cmd._name;
        if (cmd._aliases[0]) {
          cmdName = cmdName + "|" + cmd._aliases[0];
        }
        let ancestorCmdNames = "";
        for (let ancestorCmd = cmd.parent; ancestorCmd; ancestorCmd = ancestorCmd.parent) {
          ancestorCmdNames = ancestorCmd.name() + " " + ancestorCmdNames;
        }
        return ancestorCmdNames + cmdName + " " + cmd.usage();
      }
      /**
       * Get the description for the command.
       *
       * @param {Command} cmd
       * @returns {string}
       */
      commandDescription(cmd) {
        return cmd.description();
      }
      /**
       * Get the subcommand summary to show in the list of subcommands.
       * (Fallback to description for backwards compatibility.)
       *
       * @param {Command} cmd
       * @returns {string}
       */
      subcommandDescription(cmd) {
        return cmd.summary() || cmd.description();
      }
      /**
       * Get the option description to show in the list of options.
       *
       * @param {Option} option
       * @return {string}
       */
      optionDescription(option) {
        const extraInfo = [];
        if (option.argChoices) {
          extraInfo.push(
            // use stringify to match the display of the default value
            `choices: ${option.argChoices.map((choice) => JSON.stringify(choice)).join(", ")}`
          );
        }
        if (option.defaultValue !== void 0) {
          const showDefault = option.required || option.optional || option.isBoolean() && typeof option.defaultValue === "boolean";
          if (showDefault) {
            extraInfo.push(
              `default: ${option.defaultValueDescription || JSON.stringify(option.defaultValue)}`
            );
          }
        }
        if (option.presetArg !== void 0 && option.optional) {
          extraInfo.push(`preset: ${JSON.stringify(option.presetArg)}`);
        }
        if (option.envVar !== void 0) {
          extraInfo.push(`env: ${option.envVar}`);
        }
        if (extraInfo.length > 0) {
          const extraDescription = `(${extraInfo.join(", ")})`;
          if (option.description) {
            return `${option.description} ${extraDescription}`;
          }
          return extraDescription;
        }
        return option.description;
      }
      /**
       * Get the argument description to show in the list of arguments.
       *
       * @param {Argument} argument
       * @return {string}
       */
      argumentDescription(argument) {
        const extraInfo = [];
        if (argument.argChoices) {
          extraInfo.push(
            // use stringify to match the display of the default value
            `choices: ${argument.argChoices.map((choice) => JSON.stringify(choice)).join(", ")}`
          );
        }
        if (argument.defaultValue !== void 0) {
          extraInfo.push(
            `default: ${argument.defaultValueDescription || JSON.stringify(argument.defaultValue)}`
          );
        }
        if (extraInfo.length > 0) {
          const extraDescription = `(${extraInfo.join(", ")})`;
          if (argument.description) {
            return `${argument.description} ${extraDescription}`;
          }
          return extraDescription;
        }
        return argument.description;
      }
      /**
       * Format a list of items, given a heading and an array of formatted items.
       *
       * @param {string} heading
       * @param {string[]} items
       * @param {Help} helper
       * @returns string[]
       */
      formatItemList(heading, items, helper) {
        if (items.length === 0) return [];
        return [helper.styleTitle(heading), ...items, ""];
      }
      /**
       * Group items by their help group heading.
       *
       * @param {Command[] | Option[]} unsortedItems
       * @param {Command[] | Option[]} visibleItems
       * @param {Function} getGroup
       * @returns {Map<string, Command[] | Option[]>}
       */
      groupItems(unsortedItems, visibleItems, getGroup) {
        const result = /* @__PURE__ */ new Map();
        unsortedItems.forEach((item) => {
          const group = getGroup(item);
          if (!result.has(group)) result.set(group, []);
        });
        visibleItems.forEach((item) => {
          const group = getGroup(item);
          if (!result.has(group)) {
            result.set(group, []);
          }
          result.get(group).push(item);
        });
        return result;
      }
      /**
       * Generate the built-in help text.
       *
       * @param {Command} cmd
       * @param {Help} helper
       * @returns {string}
       */
      formatHelp(cmd, helper) {
        const termWidth = helper.padWidth(cmd, helper);
        const helpWidth = helper.helpWidth ?? 80;
        function callFormatItem(term, description) {
          return helper.formatItem(term, termWidth, description, helper);
        }
        let output = [
          `${helper.styleTitle("Usage:")} ${helper.styleUsage(helper.commandUsage(cmd))}`,
          ""
        ];
        const commandDescription = helper.commandDescription(cmd);
        if (commandDescription.length > 0) {
          output = output.concat([
            helper.boxWrap(
              helper.styleCommandDescription(commandDescription),
              helpWidth
            ),
            ""
          ]);
        }
        const argumentList = helper.visibleArguments(cmd).map((argument) => {
          return callFormatItem(
            helper.styleArgumentTerm(helper.argumentTerm(argument)),
            helper.styleArgumentDescription(helper.argumentDescription(argument))
          );
        });
        output = output.concat(
          this.formatItemList("Arguments:", argumentList, helper)
        );
        const optionGroups = this.groupItems(
          cmd.options,
          helper.visibleOptions(cmd),
          (option) => option.helpGroupHeading ?? "Options:"
        );
        optionGroups.forEach((options, group) => {
          const optionList = options.map((option) => {
            return callFormatItem(
              helper.styleOptionTerm(helper.optionTerm(option)),
              helper.styleOptionDescription(helper.optionDescription(option))
            );
          });
          output = output.concat(this.formatItemList(group, optionList, helper));
        });
        if (helper.showGlobalOptions) {
          const globalOptionList = helper.visibleGlobalOptions(cmd).map((option) => {
            return callFormatItem(
              helper.styleOptionTerm(helper.optionTerm(option)),
              helper.styleOptionDescription(helper.optionDescription(option))
            );
          });
          output = output.concat(
            this.formatItemList("Global Options:", globalOptionList, helper)
          );
        }
        const commandGroups = this.groupItems(
          cmd.commands,
          helper.visibleCommands(cmd),
          (sub) => sub.helpGroup() || "Commands:"
        );
        commandGroups.forEach((commands, group) => {
          const commandList = commands.map((sub) => {
            return callFormatItem(
              helper.styleSubcommandTerm(helper.subcommandTerm(sub)),
              helper.styleSubcommandDescription(helper.subcommandDescription(sub))
            );
          });
          output = output.concat(this.formatItemList(group, commandList, helper));
        });
        return output.join("\n");
      }
      /**
       * Return display width of string, ignoring ANSI escape sequences. Used in padding and wrapping calculations.
       *
       * @param {string} str
       * @returns {number}
       */
      displayWidth(str) {
        return stripColor(str).length;
      }
      /**
       * Style the title for displaying in the help. Called with 'Usage:', 'Options:', etc.
       *
       * @param {string} str
       * @returns {string}
       */
      styleTitle(str) {
        return str;
      }
      styleUsage(str) {
        return str.split(" ").map((word) => {
          if (word === "[options]") return this.styleOptionText(word);
          if (word === "[command]") return this.styleSubcommandText(word);
          if (word[0] === "[" || word[0] === "<")
            return this.styleArgumentText(word);
          return this.styleCommandText(word);
        }).join(" ");
      }
      styleCommandDescription(str) {
        return this.styleDescriptionText(str);
      }
      styleOptionDescription(str) {
        return this.styleDescriptionText(str);
      }
      styleSubcommandDescription(str) {
        return this.styleDescriptionText(str);
      }
      styleArgumentDescription(str) {
        return this.styleDescriptionText(str);
      }
      styleDescriptionText(str) {
        return str;
      }
      styleOptionTerm(str) {
        return this.styleOptionText(str);
      }
      styleSubcommandTerm(str) {
        return str.split(" ").map((word) => {
          if (word === "[options]") return this.styleOptionText(word);
          if (word[0] === "[" || word[0] === "<")
            return this.styleArgumentText(word);
          return this.styleSubcommandText(word);
        }).join(" ");
      }
      styleArgumentTerm(str) {
        return this.styleArgumentText(str);
      }
      styleOptionText(str) {
        return str;
      }
      styleArgumentText(str) {
        return str;
      }
      styleSubcommandText(str) {
        return str;
      }
      styleCommandText(str) {
        return str;
      }
      /**
       * Calculate the pad width from the maximum term length.
       *
       * @param {Command} cmd
       * @param {Help} helper
       * @returns {number}
       */
      padWidth(cmd, helper) {
        return Math.max(
          helper.longestOptionTermLength(cmd, helper),
          helper.longestGlobalOptionTermLength(cmd, helper),
          helper.longestSubcommandTermLength(cmd, helper),
          helper.longestArgumentTermLength(cmd, helper)
        );
      }
      /**
       * Detect manually wrapped and indented strings by checking for line break followed by whitespace.
       *
       * @param {string} str
       * @returns {boolean}
       */
      preformatted(str) {
        return /\n[^\S\r\n]/.test(str);
      }
      /**
       * Format the "item", which consists of a term and description. Pad the term and wrap the description, indenting the following lines.
       *
       * So "TTT", 5, "DDD DDDD DD DDD" might be formatted for this.helpWidth=17 like so:
       *   TTT  DDD DDDD
       *        DD DDD
       *
       * @param {string} term
       * @param {number} termWidth
       * @param {string} description
       * @param {Help} helper
       * @returns {string}
       */
      formatItem(term, termWidth, description, helper) {
        const itemIndent = 2;
        const itemIndentStr = " ".repeat(itemIndent);
        if (!description) return itemIndentStr + term;
        const paddedTerm = term.padEnd(
          termWidth + term.length - helper.displayWidth(term)
        );
        const spacerWidth = 2;
        const helpWidth = this.helpWidth ?? 80;
        const remainingWidth = helpWidth - termWidth - spacerWidth - itemIndent;
        let formattedDescription;
        if (remainingWidth < this.minWidthToWrap || helper.preformatted(description)) {
          formattedDescription = description;
        } else {
          const wrappedDescription = helper.boxWrap(description, remainingWidth);
          formattedDescription = wrappedDescription.replace(
            /\n/g,
            "\n" + " ".repeat(termWidth + spacerWidth)
          );
        }
        return itemIndentStr + paddedTerm + " ".repeat(spacerWidth) + formattedDescription.replace(/\n/g, `
${itemIndentStr}`);
      }
      /**
       * Wrap a string at whitespace, preserving existing line breaks.
       * Wrapping is skipped if the width is less than `minWidthToWrap`.
       *
       * @param {string} str
       * @param {number} width
       * @returns {string}
       */
      boxWrap(str, width) {
        if (width < this.minWidthToWrap) return str;
        const rawLines = str.split(/\r\n|\n/);
        const chunkPattern = /[\s]*[^\s]+/g;
        const wrappedLines = [];
        rawLines.forEach((line) => {
          const chunks = line.match(chunkPattern);
          if (chunks === null) {
            wrappedLines.push("");
            return;
          }
          let sumChunks = [chunks.shift()];
          let sumWidth = this.displayWidth(sumChunks[0]);
          chunks.forEach((chunk) => {
            const visibleWidth = this.displayWidth(chunk);
            if (sumWidth + visibleWidth <= width) {
              sumChunks.push(chunk);
              sumWidth += visibleWidth;
              return;
            }
            wrappedLines.push(sumChunks.join(""));
            const nextChunk = chunk.trimStart();
            sumChunks = [nextChunk];
            sumWidth = this.displayWidth(nextChunk);
          });
          wrappedLines.push(sumChunks.join(""));
        });
        return wrappedLines.join("\n");
      }
    };
    function stripColor(str) {
      const sgrPattern = /\x1b\[\d*(;\d*)*m/g;
      return str.replace(sgrPattern, "");
    }
    exports2.Help = Help2;
    exports2.stripColor = stripColor;
  }
});

// node_modules/commander/lib/option.js
var require_option = __commonJS({
  "node_modules/commander/lib/option.js"(exports2) {
    var { InvalidArgumentError: InvalidArgumentError2 } = require_error();
    var Option2 = class {
      /**
       * Initialize a new `Option` with the given `flags` and `description`.
       *
       * @param {string} flags
       * @param {string} [description]
       */
      constructor(flags, description) {
        this.flags = flags;
        this.description = description || "";
        this.required = flags.includes("<");
        this.optional = flags.includes("[");
        this.variadic = /\w\.\.\.[>\]]$/.test(flags);
        this.mandatory = false;
        const optionFlags = splitOptionFlags(flags);
        this.short = optionFlags.shortFlag;
        this.long = optionFlags.longFlag;
        this.negate = false;
        if (this.long) {
          this.negate = this.long.startsWith("--no-");
        }
        this.defaultValue = void 0;
        this.defaultValueDescription = void 0;
        this.presetArg = void 0;
        this.envVar = void 0;
        this.parseArg = void 0;
        this.hidden = false;
        this.argChoices = void 0;
        this.conflictsWith = [];
        this.implied = void 0;
        this.helpGroupHeading = void 0;
      }
      /**
       * Set the default value, and optionally supply the description to be displayed in the help.
       *
       * @param {*} value
       * @param {string} [description]
       * @return {Option}
       */
      default(value, description) {
        this.defaultValue = value;
        this.defaultValueDescription = description;
        return this;
      }
      /**
       * Preset to use when option used without option-argument, especially optional but also boolean and negated.
       * The custom processing (parseArg) is called.
       *
       * @example
       * new Option('--color').default('GREYSCALE').preset('RGB');
       * new Option('--donate [amount]').preset('20').argParser(parseFloat);
       *
       * @param {*} arg
       * @return {Option}
       */
      preset(arg) {
        this.presetArg = arg;
        return this;
      }
      /**
       * Add option name(s) that conflict with this option.
       * An error will be displayed if conflicting options are found during parsing.
       *
       * @example
       * new Option('--rgb').conflicts('cmyk');
       * new Option('--js').conflicts(['ts', 'jsx']);
       *
       * @param {(string | string[])} names
       * @return {Option}
       */
      conflicts(names) {
        this.conflictsWith = this.conflictsWith.concat(names);
        return this;
      }
      /**
       * Specify implied option values for when this option is set and the implied options are not.
       *
       * The custom processing (parseArg) is not called on the implied values.
       *
       * @example
       * program
       *   .addOption(new Option('--log', 'write logging information to file'))
       *   .addOption(new Option('--trace', 'log extra details').implies({ log: 'trace.txt' }));
       *
       * @param {object} impliedOptionValues
       * @return {Option}
       */
      implies(impliedOptionValues) {
        let newImplied = impliedOptionValues;
        if (typeof impliedOptionValues === "string") {
          newImplied = { [impliedOptionValues]: true };
        }
        this.implied = Object.assign(this.implied || {}, newImplied);
        return this;
      }
      /**
       * Set environment variable to check for option value.
       *
       * An environment variable is only used if when processed the current option value is
       * undefined, or the source of the current value is 'default' or 'config' or 'env'.
       *
       * @param {string} name
       * @return {Option}
       */
      env(name) {
        this.envVar = name;
        return this;
      }
      /**
       * Set the custom handler for processing CLI option arguments into option values.
       *
       * @param {Function} [fn]
       * @return {Option}
       */
      argParser(fn) {
        this.parseArg = fn;
        return this;
      }
      /**
       * Whether the option is mandatory and must have a value after parsing.
       *
       * @param {boolean} [mandatory=true]
       * @return {Option}
       */
      makeOptionMandatory(mandatory = true) {
        this.mandatory = !!mandatory;
        return this;
      }
      /**
       * Hide option in help.
       *
       * @param {boolean} [hide=true]
       * @return {Option}
       */
      hideHelp(hide = true) {
        this.hidden = !!hide;
        return this;
      }
      /**
       * @package
       */
      _collectValue(value, previous) {
        if (previous === this.defaultValue || !Array.isArray(previous)) {
          return [value];
        }
        previous.push(value);
        return previous;
      }
      /**
       * Only allow option value to be one of choices.
       *
       * @param {string[]} values
       * @return {Option}
       */
      choices(values) {
        this.argChoices = values.slice();
        this.parseArg = (arg, previous) => {
          if (!this.argChoices.includes(arg)) {
            throw new InvalidArgumentError2(
              `Allowed choices are ${this.argChoices.join(", ")}.`
            );
          }
          if (this.variadic) {
            return this._collectValue(arg, previous);
          }
          return arg;
        };
        return this;
      }
      /**
       * Return option name.
       *
       * @return {string}
       */
      name() {
        if (this.long) {
          return this.long.replace(/^--/, "");
        }
        return this.short.replace(/^-/, "");
      }
      /**
       * Return option name, in a camelcase format that can be used
       * as an object attribute key.
       *
       * @return {string}
       */
      attributeName() {
        if (this.negate) {
          return camelcase(this.name().replace(/^no-/, ""));
        }
        return camelcase(this.name());
      }
      /**
       * Set the help group heading.
       *
       * @param {string} heading
       * @return {Option}
       */
      helpGroup(heading) {
        this.helpGroupHeading = heading;
        return this;
      }
      /**
       * Check if `arg` matches the short or long flag.
       *
       * @param {string} arg
       * @return {boolean}
       * @package
       */
      is(arg) {
        return this.short === arg || this.long === arg;
      }
      /**
       * Return whether a boolean option.
       *
       * Options are one of boolean, negated, required argument, or optional argument.
       *
       * @return {boolean}
       * @package
       */
      isBoolean() {
        return !this.required && !this.optional && !this.negate;
      }
    };
    var DualOptions = class {
      /**
       * @param {Option[]} options
       */
      constructor(options) {
        this.positiveOptions = /* @__PURE__ */ new Map();
        this.negativeOptions = /* @__PURE__ */ new Map();
        this.dualOptions = /* @__PURE__ */ new Set();
        options.forEach((option) => {
          if (option.negate) {
            this.negativeOptions.set(option.attributeName(), option);
          } else {
            this.positiveOptions.set(option.attributeName(), option);
          }
        });
        this.negativeOptions.forEach((value, key) => {
          if (this.positiveOptions.has(key)) {
            this.dualOptions.add(key);
          }
        });
      }
      /**
       * Did the value come from the option, and not from possible matching dual option?
       *
       * @param {*} value
       * @param {Option} option
       * @returns {boolean}
       */
      valueFromOption(value, option) {
        const optionKey = option.attributeName();
        if (!this.dualOptions.has(optionKey)) return true;
        const preset = this.negativeOptions.get(optionKey).presetArg;
        const negativeValue = preset !== void 0 ? preset : false;
        return option.negate === (negativeValue === value);
      }
    };
    function camelcase(str) {
      return str.split("-").reduce((str2, word) => {
        return str2 + word[0].toUpperCase() + word.slice(1);
      });
    }
    function splitOptionFlags(flags) {
      let shortFlag;
      let longFlag;
      const shortFlagExp = /^-[^-]$/;
      const longFlagExp = /^--[^-]/;
      const flagParts = flags.split(/[ |,]+/).concat("guard");
      if (shortFlagExp.test(flagParts[0])) shortFlag = flagParts.shift();
      if (longFlagExp.test(flagParts[0])) longFlag = flagParts.shift();
      if (!shortFlag && shortFlagExp.test(flagParts[0]))
        shortFlag = flagParts.shift();
      if (!shortFlag && longFlagExp.test(flagParts[0])) {
        shortFlag = longFlag;
        longFlag = flagParts.shift();
      }
      if (flagParts[0].startsWith("-")) {
        const unsupportedFlag = flagParts[0];
        const baseError = `option creation failed due to '${unsupportedFlag}' in option flags '${flags}'`;
        if (/^-[^-][^-]/.test(unsupportedFlag))
          throw new Error(
            `${baseError}
- a short flag is a single dash and a single character
  - either use a single dash and a single character (for a short flag)
  - or use a double dash for a long option (and can have two, like '--ws, --workspace')`
          );
        if (shortFlagExp.test(unsupportedFlag))
          throw new Error(`${baseError}
- too many short flags`);
        if (longFlagExp.test(unsupportedFlag))
          throw new Error(`${baseError}
- too many long flags`);
        throw new Error(`${baseError}
- unrecognised flag format`);
      }
      if (shortFlag === void 0 && longFlag === void 0)
        throw new Error(
          `option creation failed due to no flags found in '${flags}'.`
        );
      return { shortFlag, longFlag };
    }
    exports2.Option = Option2;
    exports2.DualOptions = DualOptions;
  }
});

// node_modules/commander/lib/suggestSimilar.js
var require_suggestSimilar = __commonJS({
  "node_modules/commander/lib/suggestSimilar.js"(exports2) {
    var maxDistance = 3;
    function editDistance(a, b) {
      if (Math.abs(a.length - b.length) > maxDistance)
        return Math.max(a.length, b.length);
      const d = [];
      for (let i = 0; i <= a.length; i++) {
        d[i] = [i];
      }
      for (let j = 0; j <= b.length; j++) {
        d[0][j] = j;
      }
      for (let j = 1; j <= b.length; j++) {
        for (let i = 1; i <= a.length; i++) {
          let cost = 1;
          if (a[i - 1] === b[j - 1]) {
            cost = 0;
          } else {
            cost = 1;
          }
          d[i][j] = Math.min(
            d[i - 1][j] + 1,
            // deletion
            d[i][j - 1] + 1,
            // insertion
            d[i - 1][j - 1] + cost
            // substitution
          );
          if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) {
            d[i][j] = Math.min(d[i][j], d[i - 2][j - 2] + 1);
          }
        }
      }
      return d[a.length][b.length];
    }
    function suggestSimilar(word, candidates) {
      if (!candidates || candidates.length === 0) return "";
      candidates = Array.from(new Set(candidates));
      const searchingOptions = word.startsWith("--");
      if (searchingOptions) {
        word = word.slice(2);
        candidates = candidates.map((candidate) => candidate.slice(2));
      }
      let similar = [];
      let bestDistance = maxDistance;
      const minSimilarity = 0.4;
      candidates.forEach((candidate) => {
        if (candidate.length <= 1) return;
        const distance = editDistance(word, candidate);
        const length = Math.max(word.length, candidate.length);
        const similarity = (length - distance) / length;
        if (similarity > minSimilarity) {
          if (distance < bestDistance) {
            bestDistance = distance;
            similar = [candidate];
          } else if (distance === bestDistance) {
            similar.push(candidate);
          }
        }
      });
      similar.sort((a, b) => a.localeCompare(b));
      if (searchingOptions) {
        similar = similar.map((candidate) => `--${candidate}`);
      }
      if (similar.length > 1) {
        return `
(Did you mean one of ${similar.join(", ")}?)`;
      }
      if (similar.length === 1) {
        return `
(Did you mean ${similar[0]}?)`;
      }
      return "";
    }
    exports2.suggestSimilar = suggestSimilar;
  }
});

// node_modules/commander/lib/command.js
var require_command = __commonJS({
  "node_modules/commander/lib/command.js"(exports2) {
    var EventEmitter = require("node:events").EventEmitter;
    var childProcess = require("node:child_process");
    var path = require("node:path");
    var fs = require("node:fs");
    var process2 = require("node:process");
    var { Argument: Argument2, humanReadableArgName } = require_argument();
    var { CommanderError: CommanderError2 } = require_error();
    var { Help: Help2, stripColor } = require_help();
    var { Option: Option2, DualOptions } = require_option();
    var { suggestSimilar } = require_suggestSimilar();
    var Command2 = class _Command extends EventEmitter {
      /**
       * Initialize a new `Command`.
       *
       * @param {string} [name]
       */
      constructor(name) {
        super();
        this.commands = [];
        this.options = [];
        this.parent = null;
        this._allowUnknownOption = false;
        this._allowExcessArguments = false;
        this.registeredArguments = [];
        this._args = this.registeredArguments;
        this.args = [];
        this.rawArgs = [];
        this.processedArgs = [];
        this._scriptPath = null;
        this._name = name || "";
        this._optionValues = {};
        this._optionValueSources = {};
        this._storeOptionsAsProperties = false;
        this._actionHandler = null;
        this._executableHandler = false;
        this._executableFile = null;
        this._executableDir = null;
        this._defaultCommandName = null;
        this._exitCallback = null;
        this._aliases = [];
        this._combineFlagAndOptionalValue = true;
        this._description = "";
        this._summary = "";
        this._argsDescription = void 0;
        this._enablePositionalOptions = false;
        this._passThroughOptions = false;
        this._lifeCycleHooks = {};
        this._showHelpAfterError = false;
        this._showSuggestionAfterError = true;
        this._savedState = null;
        this._outputConfiguration = {
          writeOut: (str) => process2.stdout.write(str),
          writeErr: (str) => process2.stderr.write(str),
          outputError: (str, write) => write(str),
          getOutHelpWidth: () => process2.stdout.isTTY ? process2.stdout.columns : void 0,
          getErrHelpWidth: () => process2.stderr.isTTY ? process2.stderr.columns : void 0,
          getOutHasColors: () => useColor() ?? (process2.stdout.isTTY && process2.stdout.hasColors?.()),
          getErrHasColors: () => useColor() ?? (process2.stderr.isTTY && process2.stderr.hasColors?.()),
          stripColor: (str) => stripColor(str)
        };
        this._hidden = false;
        this._helpOption = void 0;
        this._addImplicitHelpCommand = void 0;
        this._helpCommand = void 0;
        this._helpConfiguration = {};
        this._helpGroupHeading = void 0;
        this._defaultCommandGroup = void 0;
        this._defaultOptionGroup = void 0;
      }
      /**
       * Copy settings that are useful to have in common across root command and subcommands.
       *
       * (Used internally when adding a command using `.command()` so subcommands inherit parent settings.)
       *
       * @param {Command} sourceCommand
       * @return {Command} `this` command for chaining
       */
      copyInheritedSettings(sourceCommand) {
        this._outputConfiguration = sourceCommand._outputConfiguration;
        this._helpOption = sourceCommand._helpOption;
        this._helpCommand = sourceCommand._helpCommand;
        this._helpConfiguration = sourceCommand._helpConfiguration;
        this._exitCallback = sourceCommand._exitCallback;
        this._storeOptionsAsProperties = sourceCommand._storeOptionsAsProperties;
        this._combineFlagAndOptionalValue = sourceCommand._combineFlagAndOptionalValue;
        this._allowExcessArguments = sourceCommand._allowExcessArguments;
        this._enablePositionalOptions = sourceCommand._enablePositionalOptions;
        this._showHelpAfterError = sourceCommand._showHelpAfterError;
        this._showSuggestionAfterError = sourceCommand._showSuggestionAfterError;
        return this;
      }
      /**
       * @returns {Command[]}
       * @private
       */
      _getCommandAndAncestors() {
        const result = [];
        for (let command = this; command; command = command.parent) {
          result.push(command);
        }
        return result;
      }
      /**
       * Define a command.
       *
       * There are two styles of command: pay attention to where to put the description.
       *
       * @example
       * // Command implemented using action handler (description is supplied separately to `.command`)
       * program
       *   .command('clone <source> [destination]')
       *   .description('clone a repository into a newly created directory')
       *   .action((source, destination) => {
       *     console.log('clone command called');
       *   });
       *
       * // Command implemented using separate executable file (description is second parameter to `.command`)
       * program
       *   .command('start <service>', 'start named service')
       *   .command('stop [service]', 'stop named service, or all if no name supplied');
       *
       * @param {string} nameAndArgs - command name and arguments, args are `<required>` or `[optional]` and last may also be `variadic...`
       * @param {(object | string)} [actionOptsOrExecDesc] - configuration options (for action), or description (for executable)
       * @param {object} [execOpts] - configuration options (for executable)
       * @return {Command} returns new command for action handler, or `this` for executable command
       */
      command(nameAndArgs, actionOptsOrExecDesc, execOpts) {
        let desc = actionOptsOrExecDesc;
        let opts = execOpts;
        if (typeof desc === "object" && desc !== null) {
          opts = desc;
          desc = null;
        }
        opts = opts || {};
        const [, name, args] = nameAndArgs.match(/([^ ]+) *(.*)/);
        const cmd = this.createCommand(name);
        if (desc) {
          cmd.description(desc);
          cmd._executableHandler = true;
        }
        if (opts.isDefault) this._defaultCommandName = cmd._name;
        cmd._hidden = !!(opts.noHelp || opts.hidden);
        cmd._executableFile = opts.executableFile || null;
        if (args) cmd.arguments(args);
        this._registerCommand(cmd);
        cmd.parent = this;
        cmd.copyInheritedSettings(this);
        if (desc) return this;
        return cmd;
      }
      /**
       * Factory routine to create a new unattached command.
       *
       * See .command() for creating an attached subcommand, which uses this routine to
       * create the command. You can override createCommand to customise subcommands.
       *
       * @param {string} [name]
       * @return {Command} new command
       */
      createCommand(name) {
        return new _Command(name);
      }
      /**
       * You can customise the help with a subclass of Help by overriding createHelp,
       * or by overriding Help properties using configureHelp().
       *
       * @return {Help}
       */
      createHelp() {
        return Object.assign(new Help2(), this.configureHelp());
      }
      /**
       * You can customise the help by overriding Help properties using configureHelp(),
       * or with a subclass of Help by overriding createHelp().
       *
       * @param {object} [configuration] - configuration options
       * @return {(Command | object)} `this` command for chaining, or stored configuration
       */
      configureHelp(configuration) {
        if (configuration === void 0) return this._helpConfiguration;
        this._helpConfiguration = configuration;
        return this;
      }
      /**
       * The default output goes to stdout and stderr. You can customise this for special
       * applications. You can also customise the display of errors by overriding outputError.
       *
       * The configuration properties are all functions:
       *
       *     // change how output being written, defaults to stdout and stderr
       *     writeOut(str)
       *     writeErr(str)
       *     // change how output being written for errors, defaults to writeErr
       *     outputError(str, write) // used for displaying errors and not used for displaying help
       *     // specify width for wrapping help
       *     getOutHelpWidth()
       *     getErrHelpWidth()
       *     // color support, currently only used with Help
       *     getOutHasColors()
       *     getErrHasColors()
       *     stripColor() // used to remove ANSI escape codes if output does not have colors
       *
       * @param {object} [configuration] - configuration options
       * @return {(Command | object)} `this` command for chaining, or stored configuration
       */
      configureOutput(configuration) {
        if (configuration === void 0) return this._outputConfiguration;
        this._outputConfiguration = {
          ...this._outputConfiguration,
          ...configuration
        };
        return this;
      }
      /**
       * Display the help or a custom message after an error occurs.
       *
       * @param {(boolean|string)} [displayHelp]
       * @return {Command} `this` command for chaining
       */
      showHelpAfterError(displayHelp = true) {
        if (typeof displayHelp !== "string") displayHelp = !!displayHelp;
        this._showHelpAfterError = displayHelp;
        return this;
      }
      /**
       * Display suggestion of similar commands for unknown commands, or options for unknown options.
       *
       * @param {boolean} [displaySuggestion]
       * @return {Command} `this` command for chaining
       */
      showSuggestionAfterError(displaySuggestion = true) {
        this._showSuggestionAfterError = !!displaySuggestion;
        return this;
      }
      /**
       * Add a prepared subcommand.
       *
       * See .command() for creating an attached subcommand which inherits settings from its parent.
       *
       * @param {Command} cmd - new subcommand
       * @param {object} [opts] - configuration options
       * @return {Command} `this` command for chaining
       */
      addCommand(cmd, opts) {
        if (!cmd._name) {
          throw new Error(`Command passed to .addCommand() must have a name
- specify the name in Command constructor or using .name()`);
        }
        opts = opts || {};
        if (opts.isDefault) this._defaultCommandName = cmd._name;
        if (opts.noHelp || opts.hidden) cmd._hidden = true;
        this._registerCommand(cmd);
        cmd.parent = this;
        cmd._checkForBrokenPassThrough();
        return this;
      }
      /**
       * Factory routine to create a new unattached argument.
       *
       * See .argument() for creating an attached argument, which uses this routine to
       * create the argument. You can override createArgument to return a custom argument.
       *
       * @param {string} name
       * @param {string} [description]
       * @return {Argument} new argument
       */
      createArgument(name, description) {
        return new Argument2(name, description);
      }
      /**
       * Define argument syntax for command.
       *
       * The default is that the argument is required, and you can explicitly
       * indicate this with <> around the name. Put [] around the name for an optional argument.
       *
       * @example
       * program.argument('<input-file>');
       * program.argument('[output-file]');
       *
       * @param {string} name
       * @param {string} [description]
       * @param {(Function|*)} [parseArg] - custom argument processing function or default value
       * @param {*} [defaultValue]
       * @return {Command} `this` command for chaining
       */
      argument(name, description, parseArg, defaultValue) {
        const argument = this.createArgument(name, description);
        if (typeof parseArg === "function") {
          argument.default(defaultValue).argParser(parseArg);
        } else {
          argument.default(parseArg);
        }
        this.addArgument(argument);
        return this;
      }
      /**
       * Define argument syntax for command, adding multiple at once (without descriptions).
       *
       * See also .argument().
       *
       * @example
       * program.arguments('<cmd> [env]');
       *
       * @param {string} names
       * @return {Command} `this` command for chaining
       */
      arguments(names) {
        names.trim().split(/ +/).forEach((detail) => {
          this.argument(detail);
        });
        return this;
      }
      /**
       * Define argument syntax for command, adding a prepared argument.
       *
       * @param {Argument} argument
       * @return {Command} `this` command for chaining
       */
      addArgument(argument) {
        const previousArgument = this.registeredArguments.slice(-1)[0];
        if (previousArgument?.variadic) {
          throw new Error(
            `only the last argument can be variadic '${previousArgument.name()}'`
          );
        }
        if (argument.required && argument.defaultValue !== void 0 && argument.parseArg === void 0) {
          throw new Error(
            `a default value for a required argument is never used: '${argument.name()}'`
          );
        }
        this.registeredArguments.push(argument);
        return this;
      }
      /**
       * Customise or override default help command. By default a help command is automatically added if your command has subcommands.
       *
       * @example
       *    program.helpCommand('help [cmd]');
       *    program.helpCommand('help [cmd]', 'show help');
       *    program.helpCommand(false); // suppress default help command
       *    program.helpCommand(true); // add help command even if no subcommands
       *
       * @param {string|boolean} enableOrNameAndArgs - enable with custom name and/or arguments, or boolean to override whether added
       * @param {string} [description] - custom description
       * @return {Command} `this` command for chaining
       */
      helpCommand(enableOrNameAndArgs, description) {
        if (typeof enableOrNameAndArgs === "boolean") {
          this._addImplicitHelpCommand = enableOrNameAndArgs;
          if (enableOrNameAndArgs && this._defaultCommandGroup) {
            this._initCommandGroup(this._getHelpCommand());
          }
          return this;
        }
        const nameAndArgs = enableOrNameAndArgs ?? "help [command]";
        const [, helpName, helpArgs] = nameAndArgs.match(/([^ ]+) *(.*)/);
        const helpDescription = description ?? "display help for command";
        const helpCommand = this.createCommand(helpName);
        helpCommand.helpOption(false);
        if (helpArgs) helpCommand.arguments(helpArgs);
        if (helpDescription) helpCommand.description(helpDescription);
        this._addImplicitHelpCommand = true;
        this._helpCommand = helpCommand;
        if (enableOrNameAndArgs || description) this._initCommandGroup(helpCommand);
        return this;
      }
      /**
       * Add prepared custom help command.
       *
       * @param {(Command|string|boolean)} helpCommand - custom help command, or deprecated enableOrNameAndArgs as for `.helpCommand()`
       * @param {string} [deprecatedDescription] - deprecated custom description used with custom name only
       * @return {Command} `this` command for chaining
       */
      addHelpCommand(helpCommand, deprecatedDescription) {
        if (typeof helpCommand !== "object") {
          this.helpCommand(helpCommand, deprecatedDescription);
          return this;
        }
        this._addImplicitHelpCommand = true;
        this._helpCommand = helpCommand;
        this._initCommandGroup(helpCommand);
        return this;
      }
      /**
       * Lazy create help command.
       *
       * @return {(Command|null)}
       * @package
       */
      _getHelpCommand() {
        const hasImplicitHelpCommand = this._addImplicitHelpCommand ?? (this.commands.length && !this._actionHandler && !this._findCommand("help"));
        if (hasImplicitHelpCommand) {
          if (this._helpCommand === void 0) {
            this.helpCommand(void 0, void 0);
          }
          return this._helpCommand;
        }
        return null;
      }
      /**
       * Add hook for life cycle event.
       *
       * @param {string} event
       * @param {Function} listener
       * @return {Command} `this` command for chaining
       */
      hook(event, listener) {
        const allowedValues = ["preSubcommand", "preAction", "postAction"];
        if (!allowedValues.includes(event)) {
          throw new Error(`Unexpected value for event passed to hook : '${event}'.
Expecting one of '${allowedValues.join("', '")}'`);
        }
        if (this._lifeCycleHooks[event]) {
          this._lifeCycleHooks[event].push(listener);
        } else {
          this._lifeCycleHooks[event] = [listener];
        }
        return this;
      }
      /**
       * Register callback to use as replacement for calling process.exit.
       *
       * @param {Function} [fn] optional callback which will be passed a CommanderError, defaults to throwing
       * @return {Command} `this` command for chaining
       */
      exitOverride(fn) {
        if (fn) {
          this._exitCallback = fn;
        } else {
          this._exitCallback = (err) => {
            if (err.code !== "commander.executeSubCommandAsync") {
              throw err;
            } else {
            }
          };
        }
        return this;
      }
      /**
       * Call process.exit, and _exitCallback if defined.
       *
       * @param {number} exitCode exit code for using with process.exit
       * @param {string} code an id string representing the error
       * @param {string} message human-readable description of the error
       * @return never
       * @private
       */
      _exit(exitCode, code, message) {
        if (this._exitCallback) {
          this._exitCallback(new CommanderError2(exitCode, code, message));
        }
        process2.exit(exitCode);
      }
      /**
       * Register callback `fn` for the command.
       *
       * @example
       * program
       *   .command('serve')
       *   .description('start service')
       *   .action(function() {
       *      // do work here
       *   });
       *
       * @param {Function} fn
       * @return {Command} `this` command for chaining
       */
      action(fn) {
        const listener = (args) => {
          const expectedArgsCount = this.registeredArguments.length;
          const actionArgs = args.slice(0, expectedArgsCount);
          if (this._storeOptionsAsProperties) {
            actionArgs[expectedArgsCount] = this;
          } else {
            actionArgs[expectedArgsCount] = this.opts();
          }
          actionArgs.push(this);
          return fn.apply(this, actionArgs);
        };
        this._actionHandler = listener;
        return this;
      }
      /**
       * Factory routine to create a new unattached option.
       *
       * See .option() for creating an attached option, which uses this routine to
       * create the option. You can override createOption to return a custom option.
       *
       * @param {string} flags
       * @param {string} [description]
       * @return {Option} new option
       */
      createOption(flags, description) {
        return new Option2(flags, description);
      }
      /**
       * Wrap parseArgs to catch 'commander.invalidArgument'.
       *
       * @param {(Option | Argument)} target
       * @param {string} value
       * @param {*} previous
       * @param {string} invalidArgumentMessage
       * @private
       */
      _callParseArg(target, value, previous, invalidArgumentMessage) {
        try {
          return target.parseArg(value, previous);
        } catch (err) {
          if (err.code === "commander.invalidArgument") {
            const message = `${invalidArgumentMessage} ${err.message}`;
            this.error(message, { exitCode: err.exitCode, code: err.code });
          }
          throw err;
        }
      }
      /**
       * Check for option flag conflicts.
       * Register option if no conflicts found, or throw on conflict.
       *
       * @param {Option} option
       * @private
       */
      _registerOption(option) {
        const matchingOption = option.short && this._findOption(option.short) || option.long && this._findOption(option.long);
        if (matchingOption) {
          const matchingFlag = option.long && this._findOption(option.long) ? option.long : option.short;
          throw new Error(`Cannot add option '${option.flags}'${this._name && ` to command '${this._name}'`} due to conflicting flag '${matchingFlag}'
-  already used by option '${matchingOption.flags}'`);
        }
        this._initOptionGroup(option);
        this.options.push(option);
      }
      /**
       * Check for command name and alias conflicts with existing commands.
       * Register command if no conflicts found, or throw on conflict.
       *
       * @param {Command} command
       * @private
       */
      _registerCommand(command) {
        const knownBy = (cmd) => {
          return [cmd.name()].concat(cmd.aliases());
        };
        const alreadyUsed = knownBy(command).find(
          (name) => this._findCommand(name)
        );
        if (alreadyUsed) {
          const existingCmd = knownBy(this._findCommand(alreadyUsed)).join("|");
          const newCmd = knownBy(command).join("|");
          throw new Error(
            `cannot add command '${newCmd}' as already have command '${existingCmd}'`
          );
        }
        this._initCommandGroup(command);
        this.commands.push(command);
      }
      /**
       * Add an option.
       *
       * @param {Option} option
       * @return {Command} `this` command for chaining
       */
      addOption(option) {
        this._registerOption(option);
        const oname = option.name();
        const name = option.attributeName();
        if (option.negate) {
          const positiveLongFlag = option.long.replace(/^--no-/, "--");
          if (!this._findOption(positiveLongFlag)) {
            this.setOptionValueWithSource(
              name,
              option.defaultValue === void 0 ? true : option.defaultValue,
              "default"
            );
          }
        } else if (option.defaultValue !== void 0) {
          this.setOptionValueWithSource(name, option.defaultValue, "default");
        }
        const handleOptionValue = (val, invalidValueMessage, valueSource) => {
          if (val == null && option.presetArg !== void 0) {
            val = option.presetArg;
          }
          const oldValue = this.getOptionValue(name);
          if (val !== null && option.parseArg) {
            val = this._callParseArg(option, val, oldValue, invalidValueMessage);
          } else if (val !== null && option.variadic) {
            val = option._collectValue(val, oldValue);
          }
          if (val == null) {
            if (option.negate) {
              val = false;
            } else if (option.isBoolean() || option.optional) {
              val = true;
            } else {
              val = "";
            }
          }
          this.setOptionValueWithSource(name, val, valueSource);
        };
        this.on("option:" + oname, (val) => {
          const invalidValueMessage = `error: option '${option.flags}' argument '${val}' is invalid.`;
          handleOptionValue(val, invalidValueMessage, "cli");
        });
        if (option.envVar) {
          this.on("optionEnv:" + oname, (val) => {
            const invalidValueMessage = `error: option '${option.flags}' value '${val}' from env '${option.envVar}' is invalid.`;
            handleOptionValue(val, invalidValueMessage, "env");
          });
        }
        return this;
      }
      /**
       * Internal implementation shared by .option() and .requiredOption()
       *
       * @return {Command} `this` command for chaining
       * @private
       */
      _optionEx(config, flags, description, fn, defaultValue) {
        if (typeof flags === "object" && flags instanceof Option2) {
          throw new Error(
            "To add an Option object use addOption() instead of option() or requiredOption()"
          );
        }
        const option = this.createOption(flags, description);
        option.makeOptionMandatory(!!config.mandatory);
        if (typeof fn === "function") {
          option.default(defaultValue).argParser(fn);
        } else if (fn instanceof RegExp) {
          const regex = fn;
          fn = (val, def) => {
            const m = regex.exec(val);
            return m ? m[0] : def;
          };
          option.default(defaultValue).argParser(fn);
        } else {
          option.default(fn);
        }
        return this.addOption(option);
      }
      /**
       * Define option with `flags`, `description`, and optional argument parsing function or `defaultValue` or both.
       *
       * The `flags` string contains the short and/or long flags, separated by comma, a pipe or space. A required
       * option-argument is indicated by `<>` and an optional option-argument by `[]`.
       *
       * See the README for more details, and see also addOption() and requiredOption().
       *
       * @example
       * program
       *     .option('-p, --pepper', 'add pepper')
       *     .option('--pt, --pizza-type <TYPE>', 'type of pizza') // required option-argument
       *     .option('-c, --cheese [CHEESE]', 'add extra cheese', 'mozzarella') // optional option-argument with default
       *     .option('-t, --tip <VALUE>', 'add tip to purchase cost', parseFloat) // custom parse function
       *
       * @param {string} flags
       * @param {string} [description]
       * @param {(Function|*)} [parseArg] - custom option processing function or default value
       * @param {*} [defaultValue]
       * @return {Command} `this` command for chaining
       */
      option(flags, description, parseArg, defaultValue) {
        return this._optionEx({}, flags, description, parseArg, defaultValue);
      }
      /**
       * Add a required option which must have a value after parsing. This usually means
       * the option must be specified on the command line. (Otherwise the same as .option().)
       *
       * The `flags` string contains the short and/or long flags, separated by comma, a pipe or space.
       *
       * @param {string} flags
       * @param {string} [description]
       * @param {(Function|*)} [parseArg] - custom option processing function or default value
       * @param {*} [defaultValue]
       * @return {Command} `this` command for chaining
       */
      requiredOption(flags, description, parseArg, defaultValue) {
        return this._optionEx(
          { mandatory: true },
          flags,
          description,
          parseArg,
          defaultValue
        );
      }
      /**
       * Alter parsing of short flags with optional values.
       *
       * @example
       * // for `.option('-f,--flag [value]'):
       * program.combineFlagAndOptionalValue(true);  // `-f80` is treated like `--flag=80`, this is the default behaviour
       * program.combineFlagAndOptionalValue(false) // `-fb` is treated like `-f -b`
       *
       * @param {boolean} [combine] - if `true` or omitted, an optional value can be specified directly after the flag.
       * @return {Command} `this` command for chaining
       */
      combineFlagAndOptionalValue(combine = true) {
        this._combineFlagAndOptionalValue = !!combine;
        return this;
      }
      /**
       * Allow unknown options on the command line.
       *
       * @param {boolean} [allowUnknown] - if `true` or omitted, no error will be thrown for unknown options.
       * @return {Command} `this` command for chaining
       */
      allowUnknownOption(allowUnknown = true) {
        this._allowUnknownOption = !!allowUnknown;
        return this;
      }
      /**
       * Allow excess command-arguments on the command line. Pass false to make excess arguments an error.
       *
       * @param {boolean} [allowExcess] - if `true` or omitted, no error will be thrown for excess arguments.
       * @return {Command} `this` command for chaining
       */
      allowExcessArguments(allowExcess = true) {
        this._allowExcessArguments = !!allowExcess;
        return this;
      }
      /**
       * Enable positional options. Positional means global options are specified before subcommands which lets
       * subcommands reuse the same option names, and also enables subcommands to turn on passThroughOptions.
       * The default behaviour is non-positional and global options may appear anywhere on the command line.
       *
       * @param {boolean} [positional]
       * @return {Command} `this` command for chaining
       */
      enablePositionalOptions(positional = true) {
        this._enablePositionalOptions = !!positional;
        return this;
      }
      /**
       * Pass through options that come after command-arguments rather than treat them as command-options,
       * so actual command-options come before command-arguments. Turning this on for a subcommand requires
       * positional options to have been enabled on the program (parent commands).
       * The default behaviour is non-positional and options may appear before or after command-arguments.
       *
       * @param {boolean} [passThrough] for unknown options.
       * @return {Command} `this` command for chaining
       */
      passThroughOptions(passThrough = true) {
        this._passThroughOptions = !!passThrough;
        this._checkForBrokenPassThrough();
        return this;
      }
      /**
       * @private
       */
      _checkForBrokenPassThrough() {
        if (this.parent && this._passThroughOptions && !this.parent._enablePositionalOptions) {
          throw new Error(
            `passThroughOptions cannot be used for '${this._name}' without turning on enablePositionalOptions for parent command(s)`
          );
        }
      }
      /**
       * Whether to store option values as properties on command object,
       * or store separately (specify false). In both cases the option values can be accessed using .opts().
       *
       * @param {boolean} [storeAsProperties=true]
       * @return {Command} `this` command for chaining
       */
      storeOptionsAsProperties(storeAsProperties = true) {
        if (this.options.length) {
          throw new Error("call .storeOptionsAsProperties() before adding options");
        }
        if (Object.keys(this._optionValues).length) {
          throw new Error(
            "call .storeOptionsAsProperties() before setting option values"
          );
        }
        this._storeOptionsAsProperties = !!storeAsProperties;
        return this;
      }
      /**
       * Retrieve option value.
       *
       * @param {string} key
       * @return {object} value
       */
      getOptionValue(key) {
        if (this._storeOptionsAsProperties) {
          return this[key];
        }
        return this._optionValues[key];
      }
      /**
       * Store option value.
       *
       * @param {string} key
       * @param {object} value
       * @return {Command} `this` command for chaining
       */
      setOptionValue(key, value) {
        return this.setOptionValueWithSource(key, value, void 0);
      }
      /**
       * Store option value and where the value came from.
       *
       * @param {string} key
       * @param {object} value
       * @param {string} source - expected values are default/config/env/cli/implied
       * @return {Command} `this` command for chaining
       */
      setOptionValueWithSource(key, value, source) {
        if (this._storeOptionsAsProperties) {
          this[key] = value;
        } else {
          this._optionValues[key] = value;
        }
        this._optionValueSources[key] = source;
        return this;
      }
      /**
       * Get source of option value.
       * Expected values are default | config | env | cli | implied
       *
       * @param {string} key
       * @return {string}
       */
      getOptionValueSource(key) {
        return this._optionValueSources[key];
      }
      /**
       * Get source of option value. See also .optsWithGlobals().
       * Expected values are default | config | env | cli | implied
       *
       * @param {string} key
       * @return {string}
       */
      getOptionValueSourceWithGlobals(key) {
        let source;
        this._getCommandAndAncestors().forEach((cmd) => {
          if (cmd.getOptionValueSource(key) !== void 0) {
            source = cmd.getOptionValueSource(key);
          }
        });
        return source;
      }
      /**
       * Get user arguments from implied or explicit arguments.
       * Side-effects: set _scriptPath if args included script. Used for default program name, and subcommand searches.
       *
       * @private
       */
      _prepareUserArgs(argv, parseOptions) {
        if (argv !== void 0 && !Array.isArray(argv)) {
          throw new Error("first parameter to parse must be array or undefined");
        }
        parseOptions = parseOptions || {};
        if (argv === void 0 && parseOptions.from === void 0) {
          if (process2.versions?.electron) {
            parseOptions.from = "electron";
          }
          const execArgv = process2.execArgv ?? [];
          if (execArgv.includes("-e") || execArgv.includes("--eval") || execArgv.includes("-p") || execArgv.includes("--print")) {
            parseOptions.from = "eval";
          }
        }
        if (argv === void 0) {
          argv = process2.argv;
        }
        this.rawArgs = argv.slice();
        let userArgs;
        switch (parseOptions.from) {
          case void 0:
          case "node":
            this._scriptPath = argv[1];
            userArgs = argv.slice(2);
            break;
          case "electron":
            if (process2.defaultApp) {
              this._scriptPath = argv[1];
              userArgs = argv.slice(2);
            } else {
              userArgs = argv.slice(1);
            }
            break;
          case "user":
            userArgs = argv.slice(0);
            break;
          case "eval":
            userArgs = argv.slice(1);
            break;
          default:
            throw new Error(
              `unexpected parse option { from: '${parseOptions.from}' }`
            );
        }
        if (!this._name && this._scriptPath)
          this.nameFromFilename(this._scriptPath);
        this._name = this._name || "program";
        return userArgs;
      }
      /**
       * Parse `argv`, setting options and invoking commands when defined.
       *
       * Use parseAsync instead of parse if any of your action handlers are async.
       *
       * Call with no parameters to parse `process.argv`. Detects Electron and special node options like `node --eval`. Easy mode!
       *
       * Or call with an array of strings to parse, and optionally where the user arguments start by specifying where the arguments are `from`:
       * - `'node'`: default, `argv[0]` is the application and `argv[1]` is the script being run, with user arguments after that
       * - `'electron'`: `argv[0]` is the application and `argv[1]` varies depending on whether the electron application is packaged
       * - `'user'`: just user arguments
       *
       * @example
       * program.parse(); // parse process.argv and auto-detect electron and special node flags
       * program.parse(process.argv); // assume argv[0] is app and argv[1] is script
       * program.parse(my-args, { from: 'user' }); // just user supplied arguments, nothing special about argv[0]
       *
       * @param {string[]} [argv] - optional, defaults to process.argv
       * @param {object} [parseOptions] - optionally specify style of options with from: node/user/electron
       * @param {string} [parseOptions.from] - where the args are from: 'node', 'user', 'electron'
       * @return {Command} `this` command for chaining
       */
      parse(argv, parseOptions) {
        this._prepareForParse();
        const userArgs = this._prepareUserArgs(argv, parseOptions);
        this._parseCommand([], userArgs);
        return this;
      }
      /**
       * Parse `argv`, setting options and invoking commands when defined.
       *
       * Call with no parameters to parse `process.argv`. Detects Electron and special node options like `node --eval`. Easy mode!
       *
       * Or call with an array of strings to parse, and optionally where the user arguments start by specifying where the arguments are `from`:
       * - `'node'`: default, `argv[0]` is the application and `argv[1]` is the script being run, with user arguments after that
       * - `'electron'`: `argv[0]` is the application and `argv[1]` varies depending on whether the electron application is packaged
       * - `'user'`: just user arguments
       *
       * @example
       * await program.parseAsync(); // parse process.argv and auto-detect electron and special node flags
       * await program.parseAsync(process.argv); // assume argv[0] is app and argv[1] is script
       * await program.parseAsync(my-args, { from: 'user' }); // just user supplied arguments, nothing special about argv[0]
       *
       * @param {string[]} [argv]
       * @param {object} [parseOptions]
       * @param {string} parseOptions.from - where the args are from: 'node', 'user', 'electron'
       * @return {Promise}
       */
      async parseAsync(argv, parseOptions) {
        this._prepareForParse();
        const userArgs = this._prepareUserArgs(argv, parseOptions);
        await this._parseCommand([], userArgs);
        return this;
      }
      _prepareForParse() {
        if (this._savedState === null) {
          this.saveStateBeforeParse();
        } else {
          this.restoreStateBeforeParse();
        }
      }
      /**
       * Called the first time parse is called to save state and allow a restore before subsequent calls to parse.
       * Not usually called directly, but available for subclasses to save their custom state.
       *
       * This is called in a lazy way. Only commands used in parsing chain will have state saved.
       */
      saveStateBeforeParse() {
        this._savedState = {
          // name is stable if supplied by author, but may be unspecified for root command and deduced during parsing
          _name: this._name,
          // option values before parse have default values (including false for negated options)
          // shallow clones
          _optionValues: { ...this._optionValues },
          _optionValueSources: { ...this._optionValueSources }
        };
      }
      /**
       * Restore state before parse for calls after the first.
       * Not usually called directly, but available for subclasses to save their custom state.
       *
       * This is called in a lazy way. Only commands used in parsing chain will have state restored.
       */
      restoreStateBeforeParse() {
        if (this._storeOptionsAsProperties)
          throw new Error(`Can not call parse again when storeOptionsAsProperties is true.
- either make a new Command for each call to parse, or stop storing options as properties`);
        this._name = this._savedState._name;
        this._scriptPath = null;
        this.rawArgs = [];
        this._optionValues = { ...this._savedState._optionValues };
        this._optionValueSources = { ...this._savedState._optionValueSources };
        this.args = [];
        this.processedArgs = [];
      }
      /**
       * Throw if expected executable is missing. Add lots of help for author.
       *
       * @param {string} executableFile
       * @param {string} executableDir
       * @param {string} subcommandName
       */
      _checkForMissingExecutable(executableFile, executableDir, subcommandName) {
        if (fs.existsSync(executableFile)) return;
        const executableDirMessage = executableDir ? `searched for local subcommand relative to directory '${executableDir}'` : "no directory for search for local subcommand, use .executableDir() to supply a custom directory";
        const executableMissing = `'${executableFile}' does not exist
 - if '${subcommandName}' is not meant to be an executable command, remove description parameter from '.command()' and use '.description()' instead
 - if the default executable name is not suitable, use the executableFile option to supply a custom name or path
 - ${executableDirMessage}`;
        throw new Error(executableMissing);
      }
      /**
       * Execute a sub-command executable.
       *
       * @private
       */
      _executeSubCommand(subcommand, args) {
        args = args.slice();
        let launchWithNode = false;
        const sourceExt = [".js", ".ts", ".tsx", ".mjs", ".cjs"];
        function findFile(baseDir, baseName) {
          const localBin = path.resolve(baseDir, baseName);
          if (fs.existsSync(localBin)) return localBin;
          if (sourceExt.includes(path.extname(baseName))) return void 0;
          const foundExt = sourceExt.find(
            (ext) => fs.existsSync(`${localBin}${ext}`)
          );
          if (foundExt) return `${localBin}${foundExt}`;
          return void 0;
        }
        this._checkForMissingMandatoryOptions();
        this._checkForConflictingOptions();
        let executableFile = subcommand._executableFile || `${this._name}-${subcommand._name}`;
        let executableDir = this._executableDir || "";
        if (this._scriptPath) {
          let resolvedScriptPath;
          try {
            resolvedScriptPath = fs.realpathSync(this._scriptPath);
          } catch {
            resolvedScriptPath = this._scriptPath;
          }
          executableDir = path.resolve(
            path.dirname(resolvedScriptPath),
            executableDir
          );
        }
        if (executableDir) {
          let localFile = findFile(executableDir, executableFile);
          if (!localFile && !subcommand._executableFile && this._scriptPath) {
            const legacyName = path.basename(
              this._scriptPath,
              path.extname(this._scriptPath)
            );
            if (legacyName !== this._name) {
              localFile = findFile(
                executableDir,
                `${legacyName}-${subcommand._name}`
              );
            }
          }
          executableFile = localFile || executableFile;
        }
        launchWithNode = sourceExt.includes(path.extname(executableFile));
        let proc;
        if (process2.platform !== "win32") {
          if (launchWithNode) {
            args.unshift(executableFile);
            args = incrementNodeInspectorPort(process2.execArgv).concat(args);
            proc = childProcess.spawn(process2.argv[0], args, { stdio: "inherit" });
          } else {
            proc = childProcess.spawn(executableFile, args, { stdio: "inherit" });
          }
        } else {
          this._checkForMissingExecutable(
            executableFile,
            executableDir,
            subcommand._name
          );
          args.unshift(executableFile);
          args = incrementNodeInspectorPort(process2.execArgv).concat(args);
          proc = childProcess.spawn(process2.execPath, args, { stdio: "inherit" });
        }
        if (!proc.killed) {
          const signals = ["SIGUSR1", "SIGUSR2", "SIGTERM", "SIGINT", "SIGHUP"];
          signals.forEach((signal) => {
            process2.on(signal, () => {
              if (proc.killed === false && proc.exitCode === null) {
                proc.kill(signal);
              }
            });
          });
        }
        const exitCallback = this._exitCallback;
        proc.on("close", (code) => {
          code = code ?? 1;
          if (!exitCallback) {
            process2.exit(code);
          } else {
            exitCallback(
              new CommanderError2(
                code,
                "commander.executeSubCommandAsync",
                "(close)"
              )
            );
          }
        });
        proc.on("error", (err) => {
          if (err.code === "ENOENT") {
            this._checkForMissingExecutable(
              executableFile,
              executableDir,
              subcommand._name
            );
          } else if (err.code === "EACCES") {
            throw new Error(`'${executableFile}' not executable`);
          }
          if (!exitCallback) {
            process2.exit(1);
          } else {
            const wrappedError = new CommanderError2(
              1,
              "commander.executeSubCommandAsync",
              "(error)"
            );
            wrappedError.nestedError = err;
            exitCallback(wrappedError);
          }
        });
        this.runningCommand = proc;
      }
      /**
       * @private
       */
      _dispatchSubcommand(commandName, operands, unknown) {
        const subCommand = this._findCommand(commandName);
        if (!subCommand) this.help({ error: true });
        subCommand._prepareForParse();
        let promiseChain;
        promiseChain = this._chainOrCallSubCommandHook(
          promiseChain,
          subCommand,
          "preSubcommand"
        );
        promiseChain = this._chainOrCall(promiseChain, () => {
          if (subCommand._executableHandler) {
            this._executeSubCommand(subCommand, operands.concat(unknown));
          } else {
            return subCommand._parseCommand(operands, unknown);
          }
        });
        return promiseChain;
      }
      /**
       * Invoke help directly if possible, or dispatch if necessary.
       * e.g. help foo
       *
       * @private
       */
      _dispatchHelpCommand(subcommandName) {
        if (!subcommandName) {
          this.help();
        }
        const subCommand = this._findCommand(subcommandName);
        if (subCommand && !subCommand._executableHandler) {
          subCommand.help();
        }
        return this._dispatchSubcommand(
          subcommandName,
          [],
          [this._getHelpOption()?.long ?? this._getHelpOption()?.short ?? "--help"]
        );
      }
      /**
       * Check this.args against expected this.registeredArguments.
       *
       * @private
       */
      _checkNumberOfArguments() {
        this.registeredArguments.forEach((arg, i) => {
          if (arg.required && this.args[i] == null) {
            this.missingArgument(arg.name());
          }
        });
        if (this.registeredArguments.length > 0 && this.registeredArguments[this.registeredArguments.length - 1].variadic) {
          return;
        }
        if (this.args.length > this.registeredArguments.length) {
          this._excessArguments(this.args);
        }
      }
      /**
       * Process this.args using this.registeredArguments and save as this.processedArgs!
       *
       * @private
       */
      _processArguments() {
        const myParseArg = (argument, value, previous) => {
          let parsedValue = value;
          if (value !== null && argument.parseArg) {
            const invalidValueMessage = `error: command-argument value '${value}' is invalid for argument '${argument.name()}'.`;
            parsedValue = this._callParseArg(
              argument,
              value,
              previous,
              invalidValueMessage
            );
          }
          return parsedValue;
        };
        this._checkNumberOfArguments();
        const processedArgs = [];
        this.registeredArguments.forEach((declaredArg, index) => {
          let value = declaredArg.defaultValue;
          if (declaredArg.variadic) {
            if (index < this.args.length) {
              value = this.args.slice(index);
              if (declaredArg.parseArg) {
                value = value.reduce((processed, v) => {
                  return myParseArg(declaredArg, v, processed);
                }, declaredArg.defaultValue);
              }
            } else if (value === void 0) {
              value = [];
            }
          } else if (index < this.args.length) {
            value = this.args[index];
            if (declaredArg.parseArg) {
              value = myParseArg(declaredArg, value, declaredArg.defaultValue);
            }
          }
          processedArgs[index] = value;
        });
        this.processedArgs = processedArgs;
      }
      /**
       * Once we have a promise we chain, but call synchronously until then.
       *
       * @param {(Promise|undefined)} promise
       * @param {Function} fn
       * @return {(Promise|undefined)}
       * @private
       */
      _chainOrCall(promise, fn) {
        if (promise?.then && typeof promise.then === "function") {
          return promise.then(() => fn());
        }
        return fn();
      }
      /**
       *
       * @param {(Promise|undefined)} promise
       * @param {string} event
       * @return {(Promise|undefined)}
       * @private
       */
      _chainOrCallHooks(promise, event) {
        let result = promise;
        const hooks = [];
        this._getCommandAndAncestors().reverse().filter((cmd) => cmd._lifeCycleHooks[event] !== void 0).forEach((hookedCommand) => {
          hookedCommand._lifeCycleHooks[event].forEach((callback) => {
            hooks.push({ hookedCommand, callback });
          });
        });
        if (event === "postAction") {
          hooks.reverse();
        }
        hooks.forEach((hookDetail) => {
          result = this._chainOrCall(result, () => {
            return hookDetail.callback(hookDetail.hookedCommand, this);
          });
        });
        return result;
      }
      /**
       *
       * @param {(Promise|undefined)} promise
       * @param {Command} subCommand
       * @param {string} event
       * @return {(Promise|undefined)}
       * @private
       */
      _chainOrCallSubCommandHook(promise, subCommand, event) {
        let result = promise;
        if (this._lifeCycleHooks[event] !== void 0) {
          this._lifeCycleHooks[event].forEach((hook) => {
            result = this._chainOrCall(result, () => {
              return hook(this, subCommand);
            });
          });
        }
        return result;
      }
      /**
       * Process arguments in context of this command.
       * Returns action result, in case it is a promise.
       *
       * @private
       */
      _parseCommand(operands, unknown) {
        const parsed = this.parseOptions(unknown);
        this._parseOptionsEnv();
        this._parseOptionsImplied();
        operands = operands.concat(parsed.operands);
        unknown = parsed.unknown;
        this.args = operands.concat(unknown);
        if (operands && this._findCommand(operands[0])) {
          return this._dispatchSubcommand(operands[0], operands.slice(1), unknown);
        }
        if (this._getHelpCommand() && operands[0] === this._getHelpCommand().name()) {
          return this._dispatchHelpCommand(operands[1]);
        }
        if (this._defaultCommandName) {
          this._outputHelpIfRequested(unknown);
          return this._dispatchSubcommand(
            this._defaultCommandName,
            operands,
            unknown
          );
        }
        if (this.commands.length && this.args.length === 0 && !this._actionHandler && !this._defaultCommandName) {
          this.help({ error: true });
        }
        this._outputHelpIfRequested(parsed.unknown);
        this._checkForMissingMandatoryOptions();
        this._checkForConflictingOptions();
        const checkForUnknownOptions = () => {
          if (parsed.unknown.length > 0) {
            this.unknownOption(parsed.unknown[0]);
          }
        };
        const commandEvent = `command:${this.name()}`;
        if (this._actionHandler) {
          checkForUnknownOptions();
          this._processArguments();
          let promiseChain;
          promiseChain = this._chainOrCallHooks(promiseChain, "preAction");
          promiseChain = this._chainOrCall(
            promiseChain,
            () => this._actionHandler(this.processedArgs)
          );
          if (this.parent) {
            promiseChain = this._chainOrCall(promiseChain, () => {
              this.parent.emit(commandEvent, operands, unknown);
            });
          }
          promiseChain = this._chainOrCallHooks(promiseChain, "postAction");
          return promiseChain;
        }
        if (this.parent?.listenerCount(commandEvent)) {
          checkForUnknownOptions();
          this._processArguments();
          this.parent.emit(commandEvent, operands, unknown);
        } else if (operands.length) {
          if (this._findCommand("*")) {
            return this._dispatchSubcommand("*", operands, unknown);
          }
          if (this.listenerCount("command:*")) {
            this.emit("command:*", operands, unknown);
          } else if (this.commands.length) {
            this.unknownCommand();
          } else {
            checkForUnknownOptions();
            this._processArguments();
          }
        } else if (this.commands.length) {
          checkForUnknownOptions();
          this.help({ error: true });
        } else {
          checkForUnknownOptions();
          this._processArguments();
        }
      }
      /**
       * Find matching command.
       *
       * @private
       * @return {Command | undefined}
       */
      _findCommand(name) {
        if (!name) return void 0;
        return this.commands.find(
          (cmd) => cmd._name === name || cmd._aliases.includes(name)
        );
      }
      /**
       * Return an option matching `arg` if any.
       *
       * @param {string} arg
       * @return {Option}
       * @package
       */
      _findOption(arg) {
        return this.options.find((option) => option.is(arg));
      }
      /**
       * Display an error message if a mandatory option does not have a value.
       * Called after checking for help flags in leaf subcommand.
       *
       * @private
       */
      _checkForMissingMandatoryOptions() {
        this._getCommandAndAncestors().forEach((cmd) => {
          cmd.options.forEach((anOption) => {
            if (anOption.mandatory && cmd.getOptionValue(anOption.attributeName()) === void 0) {
              cmd.missingMandatoryOptionValue(anOption);
            }
          });
        });
      }
      /**
       * Display an error message if conflicting options are used together in this.
       *
       * @private
       */
      _checkForConflictingLocalOptions() {
        const definedNonDefaultOptions = this.options.filter((option) => {
          const optionKey = option.attributeName();
          if (this.getOptionValue(optionKey) === void 0) {
            return false;
          }
          return this.getOptionValueSource(optionKey) !== "default";
        });
        const optionsWithConflicting = definedNonDefaultOptions.filter(
          (option) => option.conflictsWith.length > 0
        );
        optionsWithConflicting.forEach((option) => {
          const conflictingAndDefined = definedNonDefaultOptions.find(
            (defined) => option.conflictsWith.includes(defined.attributeName())
          );
          if (conflictingAndDefined) {
            this._conflictingOption(option, conflictingAndDefined);
          }
        });
      }
      /**
       * Display an error message if conflicting options are used together.
       * Called after checking for help flags in leaf subcommand.
       *
       * @private
       */
      _checkForConflictingOptions() {
        this._getCommandAndAncestors().forEach((cmd) => {
          cmd._checkForConflictingLocalOptions();
        });
      }
      /**
       * Parse options from `argv` removing known options,
       * and return argv split into operands and unknown arguments.
       *
       * Side effects: modifies command by storing options. Does not reset state if called again.
       *
       * Examples:
       *
       *     argv => operands, unknown
       *     --known kkk op => [op], []
       *     op --known kkk => [op], []
       *     sub --unknown uuu op => [sub], [--unknown uuu op]
       *     sub -- --unknown uuu op => [sub --unknown uuu op], []
       *
       * @param {string[]} args
       * @return {{operands: string[], unknown: string[]}}
       */
      parseOptions(args) {
        const operands = [];
        const unknown = [];
        let dest = operands;
        function maybeOption(arg) {
          return arg.length > 1 && arg[0] === "-";
        }
        const negativeNumberArg = (arg) => {
          if (!/^-(\d+|\d*\.\d+)(e[+-]?\d+)?$/.test(arg)) return false;
          return !this._getCommandAndAncestors().some(
            (cmd) => cmd.options.map((opt) => opt.short).some((short) => /^-\d$/.test(short))
          );
        };
        let activeVariadicOption = null;
        let activeGroup = null;
        let i = 0;
        while (i < args.length || activeGroup) {
          const arg = activeGroup ?? args[i++];
          activeGroup = null;
          if (arg === "--") {
            if (dest === unknown) dest.push(arg);
            dest.push(...args.slice(i));
            break;
          }
          if (activeVariadicOption && (!maybeOption(arg) || negativeNumberArg(arg))) {
            this.emit(`option:${activeVariadicOption.name()}`, arg);
            continue;
          }
          activeVariadicOption = null;
          if (maybeOption(arg)) {
            const option = this._findOption(arg);
            if (option) {
              if (option.required) {
                const value = args[i++];
                if (value === void 0) this.optionMissingArgument(option);
                this.emit(`option:${option.name()}`, value);
              } else if (option.optional) {
                let value = null;
                if (i < args.length && (!maybeOption(args[i]) || negativeNumberArg(args[i]))) {
                  value = args[i++];
                }
                this.emit(`option:${option.name()}`, value);
              } else {
                this.emit(`option:${option.name()}`);
              }
              activeVariadicOption = option.variadic ? option : null;
              continue;
            }
          }
          if (arg.length > 2 && arg[0] === "-" && arg[1] !== "-") {
            const option = this._findOption(`-${arg[1]}`);
            if (option) {
              if (option.required || option.optional && this._combineFlagAndOptionalValue) {
                this.emit(`option:${option.name()}`, arg.slice(2));
              } else {
                this.emit(`option:${option.name()}`);
                activeGroup = `-${arg.slice(2)}`;
              }
              continue;
            }
          }
          if (/^--[^=]+=/.test(arg)) {
            const index = arg.indexOf("=");
            const option = this._findOption(arg.slice(0, index));
            if (option && (option.required || option.optional)) {
              this.emit(`option:${option.name()}`, arg.slice(index + 1));
              continue;
            }
          }
          if (dest === operands && maybeOption(arg) && !(this.commands.length === 0 && negativeNumberArg(arg))) {
            dest = unknown;
          }
          if ((this._enablePositionalOptions || this._passThroughOptions) && operands.length === 0 && unknown.length === 0) {
            if (this._findCommand(arg)) {
              operands.push(arg);
              unknown.push(...args.slice(i));
              break;
            } else if (this._getHelpCommand() && arg === this._getHelpCommand().name()) {
              operands.push(arg, ...args.slice(i));
              break;
            } else if (this._defaultCommandName) {
              unknown.push(arg, ...args.slice(i));
              break;
            }
          }
          if (this._passThroughOptions) {
            dest.push(arg, ...args.slice(i));
            break;
          }
          dest.push(arg);
        }
        return { operands, unknown };
      }
      /**
       * Return an object containing local option values as key-value pairs.
       *
       * @return {object}
       */
      opts() {
        if (this._storeOptionsAsProperties) {
          const result = {};
          const len = this.options.length;
          for (let i = 0; i < len; i++) {
            const key = this.options[i].attributeName();
            result[key] = key === this._versionOptionName ? this._version : this[key];
          }
          return result;
        }
        return this._optionValues;
      }
      /**
       * Return an object containing merged local and global option values as key-value pairs.
       *
       * @return {object}
       */
      optsWithGlobals() {
        return this._getCommandAndAncestors().reduce(
          (combinedOptions, cmd) => Object.assign(combinedOptions, cmd.opts()),
          {}
        );
      }
      /**
       * Display error message and exit (or call exitOverride).
       *
       * @param {string} message
       * @param {object} [errorOptions]
       * @param {string} [errorOptions.code] - an id string representing the error
       * @param {number} [errorOptions.exitCode] - used with process.exit
       */
      error(message, errorOptions) {
        this._outputConfiguration.outputError(
          `${message}
`,
          this._outputConfiguration.writeErr
        );
        if (typeof this._showHelpAfterError === "string") {
          this._outputConfiguration.writeErr(`${this._showHelpAfterError}
`);
        } else if (this._showHelpAfterError) {
          this._outputConfiguration.writeErr("\n");
          this.outputHelp({ error: true });
        }
        const config = errorOptions || {};
        const exitCode = config.exitCode || 1;
        const code = config.code || "commander.error";
        this._exit(exitCode, code, message);
      }
      /**
       * Apply any option related environment variables, if option does
       * not have a value from cli or client code.
       *
       * @private
       */
      _parseOptionsEnv() {
        this.options.forEach((option) => {
          if (option.envVar && option.envVar in process2.env) {
            const optionKey = option.attributeName();
            if (this.getOptionValue(optionKey) === void 0 || ["default", "config", "env"].includes(
              this.getOptionValueSource(optionKey)
            )) {
              if (option.required || option.optional) {
                this.emit(`optionEnv:${option.name()}`, process2.env[option.envVar]);
              } else {
                this.emit(`optionEnv:${option.name()}`);
              }
            }
          }
        });
      }
      /**
       * Apply any implied option values, if option is undefined or default value.
       *
       * @private
       */
      _parseOptionsImplied() {
        const dualHelper = new DualOptions(this.options);
        const hasCustomOptionValue = (optionKey) => {
          return this.getOptionValue(optionKey) !== void 0 && !["default", "implied"].includes(this.getOptionValueSource(optionKey));
        };
        this.options.filter(
          (option) => option.implied !== void 0 && hasCustomOptionValue(option.attributeName()) && dualHelper.valueFromOption(
            this.getOptionValue(option.attributeName()),
            option
          )
        ).forEach((option) => {
          Object.keys(option.implied).filter((impliedKey) => !hasCustomOptionValue(impliedKey)).forEach((impliedKey) => {
            this.setOptionValueWithSource(
              impliedKey,
              option.implied[impliedKey],
              "implied"
            );
          });
        });
      }
      /**
       * Argument `name` is missing.
       *
       * @param {string} name
       * @private
       */
      missingArgument(name) {
        const message = `error: missing required argument '${name}'`;
        this.error(message, { code: "commander.missingArgument" });
      }
      /**
       * `Option` is missing an argument.
       *
       * @param {Option} option
       * @private
       */
      optionMissingArgument(option) {
        const message = `error: option '${option.flags}' argument missing`;
        this.error(message, { code: "commander.optionMissingArgument" });
      }
      /**
       * `Option` does not have a value, and is a mandatory option.
       *
       * @param {Option} option
       * @private
       */
      missingMandatoryOptionValue(option) {
        const message = `error: required option '${option.flags}' not specified`;
        this.error(message, { code: "commander.missingMandatoryOptionValue" });
      }
      /**
       * `Option` conflicts with another option.
       *
       * @param {Option} option
       * @param {Option} conflictingOption
       * @private
       */
      _conflictingOption(option, conflictingOption) {
        const findBestOptionFromValue = (option2) => {
          const optionKey = option2.attributeName();
          const optionValue = this.getOptionValue(optionKey);
          const negativeOption = this.options.find(
            (target) => target.negate && optionKey === target.attributeName()
          );
          const positiveOption = this.options.find(
            (target) => !target.negate && optionKey === target.attributeName()
          );
          if (negativeOption && (negativeOption.presetArg === void 0 && optionValue === false || negativeOption.presetArg !== void 0 && optionValue === negativeOption.presetArg)) {
            return negativeOption;
          }
          return positiveOption || option2;
        };
        const getErrorMessage = (option2) => {
          const bestOption = findBestOptionFromValue(option2);
          const optionKey = bestOption.attributeName();
          const source = this.getOptionValueSource(optionKey);
          if (source === "env") {
            return `environment variable '${bestOption.envVar}'`;
          }
          return `option '${bestOption.flags}'`;
        };
        const message = `error: ${getErrorMessage(option)} cannot be used with ${getErrorMessage(conflictingOption)}`;
        this.error(message, { code: "commander.conflictingOption" });
      }
      /**
       * Unknown option `flag`.
       *
       * @param {string} flag
       * @private
       */
      unknownOption(flag) {
        if (this._allowUnknownOption) return;
        let suggestion = "";
        if (flag.startsWith("--") && this._showSuggestionAfterError) {
          let candidateFlags = [];
          let command = this;
          do {
            const moreFlags = command.createHelp().visibleOptions(command).filter((option) => option.long).map((option) => option.long);
            candidateFlags = candidateFlags.concat(moreFlags);
            command = command.parent;
          } while (command && !command._enablePositionalOptions);
          suggestion = suggestSimilar(flag, candidateFlags);
        }
        const message = `error: unknown option '${flag}'${suggestion}`;
        this.error(message, { code: "commander.unknownOption" });
      }
      /**
       * Excess arguments, more than expected.
       *
       * @param {string[]} receivedArgs
       * @private
       */
      _excessArguments(receivedArgs) {
        if (this._allowExcessArguments) return;
        const expected = this.registeredArguments.length;
        const s = expected === 1 ? "" : "s";
        const forSubcommand = this.parent ? ` for '${this.name()}'` : "";
        const message = `error: too many arguments${forSubcommand}. Expected ${expected} argument${s} but got ${receivedArgs.length}.`;
        this.error(message, { code: "commander.excessArguments" });
      }
      /**
       * Unknown command.
       *
       * @private
       */
      unknownCommand() {
        const unknownName = this.args[0];
        let suggestion = "";
        if (this._showSuggestionAfterError) {
          const candidateNames = [];
          this.createHelp().visibleCommands(this).forEach((command) => {
            candidateNames.push(command.name());
            if (command.alias()) candidateNames.push(command.alias());
          });
          suggestion = suggestSimilar(unknownName, candidateNames);
        }
        const message = `error: unknown command '${unknownName}'${suggestion}`;
        this.error(message, { code: "commander.unknownCommand" });
      }
      /**
       * Get or set the program version.
       *
       * This method auto-registers the "-V, --version" option which will print the version number.
       *
       * You can optionally supply the flags and description to override the defaults.
       *
       * @param {string} [str]
       * @param {string} [flags]
       * @param {string} [description]
       * @return {(this | string | undefined)} `this` command for chaining, or version string if no arguments
       */
      version(str, flags, description) {
        if (str === void 0) return this._version;
        this._version = str;
        flags = flags || "-V, --version";
        description = description || "output the version number";
        const versionOption = this.createOption(flags, description);
        this._versionOptionName = versionOption.attributeName();
        this._registerOption(versionOption);
        this.on("option:" + versionOption.name(), () => {
          this._outputConfiguration.writeOut(`${str}
`);
          this._exit(0, "commander.version", str);
        });
        return this;
      }
      /**
       * Set the description.
       *
       * @param {string} [str]
       * @param {object} [argsDescription]
       * @return {(string|Command)}
       */
      description(str, argsDescription) {
        if (str === void 0 && argsDescription === void 0)
          return this._description;
        this._description = str;
        if (argsDescription) {
          this._argsDescription = argsDescription;
        }
        return this;
      }
      /**
       * Set the summary. Used when listed as subcommand of parent.
       *
       * @param {string} [str]
       * @return {(string|Command)}
       */
      summary(str) {
        if (str === void 0) return this._summary;
        this._summary = str;
        return this;
      }
      /**
       * Set an alias for the command.
       *
       * You may call more than once to add multiple aliases. Only the first alias is shown in the auto-generated help.
       *
       * @param {string} [alias]
       * @return {(string|Command)}
       */
      alias(alias) {
        if (alias === void 0) return this._aliases[0];
        let command = this;
        if (this.commands.length !== 0 && this.commands[this.commands.length - 1]._executableHandler) {
          command = this.commands[this.commands.length - 1];
        }
        if (alias === command._name)
          throw new Error("Command alias can't be the same as its name");
        const matchingCommand = this.parent?._findCommand(alias);
        if (matchingCommand) {
          const existingCmd = [matchingCommand.name()].concat(matchingCommand.aliases()).join("|");
          throw new Error(
            `cannot add alias '${alias}' to command '${this.name()}' as already have command '${existingCmd}'`
          );
        }
        command._aliases.push(alias);
        return this;
      }
      /**
       * Set aliases for the command.
       *
       * Only the first alias is shown in the auto-generated help.
       *
       * @param {string[]} [aliases]
       * @return {(string[]|Command)}
       */
      aliases(aliases) {
        if (aliases === void 0) return this._aliases;
        aliases.forEach((alias) => this.alias(alias));
        return this;
      }
      /**
       * Set / get the command usage `str`.
       *
       * @param {string} [str]
       * @return {(string|Command)}
       */
      usage(str) {
        if (str === void 0) {
          if (this._usage) return this._usage;
          const args = this.registeredArguments.map((arg) => {
            return humanReadableArgName(arg);
          });
          return [].concat(
            this.options.length || this._helpOption !== null ? "[options]" : [],
            this.commands.length ? "[command]" : [],
            this.registeredArguments.length ? args : []
          ).join(" ");
        }
        this._usage = str;
        return this;
      }
      /**
       * Get or set the name of the command.
       *
       * @param {string} [str]
       * @return {(string|Command)}
       */
      name(str) {
        if (str === void 0) return this._name;
        this._name = str;
        return this;
      }
      /**
       * Set/get the help group heading for this subcommand in parent command's help.
       *
       * @param {string} [heading]
       * @return {Command | string}
       */
      helpGroup(heading) {
        if (heading === void 0) return this._helpGroupHeading ?? "";
        this._helpGroupHeading = heading;
        return this;
      }
      /**
       * Set/get the default help group heading for subcommands added to this command.
       * (This does not override a group set directly on the subcommand using .helpGroup().)
       *
       * @example
       * program.commandsGroup('Development Commands:);
       * program.command('watch')...
       * program.command('lint')...
       * ...
       *
       * @param {string} [heading]
       * @returns {Command | string}
       */
      commandsGroup(heading) {
        if (heading === void 0) return this._defaultCommandGroup ?? "";
        this._defaultCommandGroup = heading;
        return this;
      }
      /**
       * Set/get the default help group heading for options added to this command.
       * (This does not override a group set directly on the option using .helpGroup().)
       *
       * @example
       * program
       *   .optionsGroup('Development Options:')
       *   .option('-d, --debug', 'output extra debugging')
       *   .option('-p, --profile', 'output profiling information')
       *
       * @param {string} [heading]
       * @returns {Command | string}
       */
      optionsGroup(heading) {
        if (heading === void 0) return this._defaultOptionGroup ?? "";
        this._defaultOptionGroup = heading;
        return this;
      }
      /**
       * @param {Option} option
       * @private
       */
      _initOptionGroup(option) {
        if (this._defaultOptionGroup && !option.helpGroupHeading)
          option.helpGroup(this._defaultOptionGroup);
      }
      /**
       * @param {Command} cmd
       * @private
       */
      _initCommandGroup(cmd) {
        if (this._defaultCommandGroup && !cmd.helpGroup())
          cmd.helpGroup(this._defaultCommandGroup);
      }
      /**
       * Set the name of the command from script filename, such as process.argv[1],
       * or require.main.filename, or __filename.
       *
       * (Used internally and public although not documented in README.)
       *
       * @example
       * program.nameFromFilename(require.main.filename);
       *
       * @param {string} filename
       * @return {Command}
       */
      nameFromFilename(filename) {
        this._name = path.basename(filename, path.extname(filename));
        return this;
      }
      /**
       * Get or set the directory for searching for executable subcommands of this command.
       *
       * @example
       * program.executableDir(__dirname);
       * // or
       * program.executableDir('subcommands');
       *
       * @param {string} [path]
       * @return {(string|null|Command)}
       */
      executableDir(path2) {
        if (path2 === void 0) return this._executableDir;
        this._executableDir = path2;
        return this;
      }
      /**
       * Return program help documentation.
       *
       * @param {{ error: boolean }} [contextOptions] - pass {error:true} to wrap for stderr instead of stdout
       * @return {string}
       */
      helpInformation(contextOptions) {
        const helper = this.createHelp();
        const context = this._getOutputContext(contextOptions);
        helper.prepareContext({
          error: context.error,
          helpWidth: context.helpWidth,
          outputHasColors: context.hasColors
        });
        const text = helper.formatHelp(this, helper);
        if (context.hasColors) return text;
        return this._outputConfiguration.stripColor(text);
      }
      /**
       * @typedef HelpContext
       * @type {object}
       * @property {boolean} error
       * @property {number} helpWidth
       * @property {boolean} hasColors
       * @property {function} write - includes stripColor if needed
       *
       * @returns {HelpContext}
       * @private
       */
      _getOutputContext(contextOptions) {
        contextOptions = contextOptions || {};
        const error = !!contextOptions.error;
        let baseWrite;
        let hasColors;
        let helpWidth;
        if (error) {
          baseWrite = (str) => this._outputConfiguration.writeErr(str);
          hasColors = this._outputConfiguration.getErrHasColors();
          helpWidth = this._outputConfiguration.getErrHelpWidth();
        } else {
          baseWrite = (str) => this._outputConfiguration.writeOut(str);
          hasColors = this._outputConfiguration.getOutHasColors();
          helpWidth = this._outputConfiguration.getOutHelpWidth();
        }
        const write = (str) => {
          if (!hasColors) str = this._outputConfiguration.stripColor(str);
          return baseWrite(str);
        };
        return { error, write, hasColors, helpWidth };
      }
      /**
       * Output help information for this command.
       *
       * Outputs built-in help, and custom text added using `.addHelpText()`.
       *
       * @param {{ error: boolean } | Function} [contextOptions] - pass {error:true} to write to stderr instead of stdout
       */
      outputHelp(contextOptions) {
        let deprecatedCallback;
        if (typeof contextOptions === "function") {
          deprecatedCallback = contextOptions;
          contextOptions = void 0;
        }
        const outputContext = this._getOutputContext(contextOptions);
        const eventContext = {
          error: outputContext.error,
          write: outputContext.write,
          command: this
        };
        this._getCommandAndAncestors().reverse().forEach((command) => command.emit("beforeAllHelp", eventContext));
        this.emit("beforeHelp", eventContext);
        let helpInformation = this.helpInformation({ error: outputContext.error });
        if (deprecatedCallback) {
          helpInformation = deprecatedCallback(helpInformation);
          if (typeof helpInformation !== "string" && !Buffer.isBuffer(helpInformation)) {
            throw new Error("outputHelp callback must return a string or a Buffer");
          }
        }
        outputContext.write(helpInformation);
        if (this._getHelpOption()?.long) {
          this.emit(this._getHelpOption().long);
        }
        this.emit("afterHelp", eventContext);
        this._getCommandAndAncestors().forEach(
          (command) => command.emit("afterAllHelp", eventContext)
        );
      }
      /**
       * You can pass in flags and a description to customise the built-in help option.
       * Pass in false to disable the built-in help option.
       *
       * @example
       * program.helpOption('-?, --help' 'show help'); // customise
       * program.helpOption(false); // disable
       *
       * @param {(string | boolean)} flags
       * @param {string} [description]
       * @return {Command} `this` command for chaining
       */
      helpOption(flags, description) {
        if (typeof flags === "boolean") {
          if (flags) {
            if (this._helpOption === null) this._helpOption = void 0;
            if (this._defaultOptionGroup) {
              this._initOptionGroup(this._getHelpOption());
            }
          } else {
            this._helpOption = null;
          }
          return this;
        }
        this._helpOption = this.createOption(
          flags ?? "-h, --help",
          description ?? "display help for command"
        );
        if (flags || description) this._initOptionGroup(this._helpOption);
        return this;
      }
      /**
       * Lazy create help option.
       * Returns null if has been disabled with .helpOption(false).
       *
       * @returns {(Option | null)} the help option
       * @package
       */
      _getHelpOption() {
        if (this._helpOption === void 0) {
          this.helpOption(void 0, void 0);
        }
        return this._helpOption;
      }
      /**
       * Supply your own option to use for the built-in help option.
       * This is an alternative to using helpOption() to customise the flags and description etc.
       *
       * @param {Option} option
       * @return {Command} `this` command for chaining
       */
      addHelpOption(option) {
        this._helpOption = option;
        this._initOptionGroup(option);
        return this;
      }
      /**
       * Output help information and exit.
       *
       * Outputs built-in help, and custom text added using `.addHelpText()`.
       *
       * @param {{ error: boolean }} [contextOptions] - pass {error:true} to write to stderr instead of stdout
       */
      help(contextOptions) {
        this.outputHelp(contextOptions);
        let exitCode = Number(process2.exitCode ?? 0);
        if (exitCode === 0 && contextOptions && typeof contextOptions !== "function" && contextOptions.error) {
          exitCode = 1;
        }
        this._exit(exitCode, "commander.help", "(outputHelp)");
      }
      /**
       * // Do a little typing to coordinate emit and listener for the help text events.
       * @typedef HelpTextEventContext
       * @type {object}
       * @property {boolean} error
       * @property {Command} command
       * @property {function} write
       */
      /**
       * Add additional text to be displayed with the built-in help.
       *
       * Position is 'before' or 'after' to affect just this command,
       * and 'beforeAll' or 'afterAll' to affect this command and all its subcommands.
       *
       * @param {string} position - before or after built-in help
       * @param {(string | Function)} text - string to add, or a function returning a string
       * @return {Command} `this` command for chaining
       */
      addHelpText(position, text) {
        const allowedValues = ["beforeAll", "before", "after", "afterAll"];
        if (!allowedValues.includes(position)) {
          throw new Error(`Unexpected value for position to addHelpText.
Expecting one of '${allowedValues.join("', '")}'`);
        }
        const helpEvent = `${position}Help`;
        this.on(helpEvent, (context) => {
          let helpStr;
          if (typeof text === "function") {
            helpStr = text({ error: context.error, command: context.command });
          } else {
            helpStr = text;
          }
          if (helpStr) {
            context.write(`${helpStr}
`);
          }
        });
        return this;
      }
      /**
       * Output help information if help flags specified
       *
       * @param {Array} args - array of options to search for help flags
       * @private
       */
      _outputHelpIfRequested(args) {
        const helpOption = this._getHelpOption();
        const helpRequested = helpOption && args.find((arg) => helpOption.is(arg));
        if (helpRequested) {
          this.outputHelp();
          this._exit(0, "commander.helpDisplayed", "(outputHelp)");
        }
      }
    };
    function incrementNodeInspectorPort(args) {
      return args.map((arg) => {
        if (!arg.startsWith("--inspect")) {
          return arg;
        }
        let debugOption;
        let debugHost = "127.0.0.1";
        let debugPort = "9229";
        let match;
        if ((match = arg.match(/^(--inspect(-brk)?)$/)) !== null) {
          debugOption = match[1];
        } else if ((match = arg.match(/^(--inspect(-brk|-port)?)=([^:]+)$/)) !== null) {
          debugOption = match[1];
          if (/^\d+$/.test(match[3])) {
            debugPort = match[3];
          } else {
            debugHost = match[3];
          }
        } else if ((match = arg.match(/^(--inspect(-brk|-port)?)=([^:]+):(\d+)$/)) !== null) {
          debugOption = match[1];
          debugHost = match[3];
          debugPort = match[4];
        }
        if (debugOption && debugPort !== "0") {
          return `${debugOption}=${debugHost}:${parseInt(debugPort) + 1}`;
        }
        return arg;
      });
    }
    function useColor() {
      if (process2.env.NO_COLOR || process2.env.FORCE_COLOR === "0" || process2.env.FORCE_COLOR === "false")
        return false;
      if (process2.env.FORCE_COLOR || process2.env.CLICOLOR_FORCE !== void 0)
        return true;
      return void 0;
    }
    exports2.Command = Command2;
    exports2.useColor = useColor;
  }
});

// node_modules/commander/index.js
var require_commander = __commonJS({
  "node_modules/commander/index.js"(exports2) {
    var { Argument: Argument2 } = require_argument();
    var { Command: Command2 } = require_command();
    var { CommanderError: CommanderError2, InvalidArgumentError: InvalidArgumentError2 } = require_error();
    var { Help: Help2 } = require_help();
    var { Option: Option2 } = require_option();
    exports2.program = new Command2();
    exports2.createCommand = (name) => new Command2(name);
    exports2.createOption = (flags, description) => new Option2(flags, description);
    exports2.createArgument = (name, description) => new Argument2(name, description);
    exports2.Command = Command2;
    exports2.Option = Option2;
    exports2.Argument = Argument2;
    exports2.Help = Help2;
    exports2.CommanderError = CommanderError2;
    exports2.InvalidArgumentError = InvalidArgumentError2;
    exports2.InvalidOptionArgumentError = InvalidArgumentError2;
  }
});

// src/cli.ts
var cli_exports = {};
__export(cli_exports, {
  PROJECT_LOCAL_CLI_IN_PROCESS_MARKER: () => PROJECT_LOCAL_CLI_IN_PROCESS_MARKER,
  getInstalledVersion: () => getInstalledVersion,
  getUpdatePackageSpec: () => getUpdatePackageSpec,
  runCli: () => runCli,
  tryHandleFastExecuteDynamicCodeCommand: () => tryHandleFastExecuteDynamicCodeCommand,
  tryParseFastExecuteDynamicCodeCommand: () => tryParseFastExecuteDynamicCodeCommand,
  updateCli: () => updateCli
});
module.exports = __toCommonJS(cli_exports);

// src/cli-constants.ts
var PRODUCT_DISPLAY_NAME = "Unity CLI Loop";
var MENU_PATH_SERVER = "Window > Unity CLI Loop > Server";

// src/cli.ts
var import_fs10 = require("fs");
var import_path12 = require("path");
var import_os2 = require("os");
var import_child_process2 = require("child_process");

// node_modules/commander/esm.mjs
var import_index = __toESM(require_commander(), 1);
var {
  program,
  createCommand,
  createArgument,
  createOption,
  CommanderError,
  InvalidArgumentError,
  InvalidOptionArgumentError,
  // deprecated old name
  Command,
  Argument,
  Option,
  Help
} = import_index.default;

// src/execute-tool.ts
var readline = __toESM(require("readline"), 1);
var import_child_process = require("child_process");
var import_fs6 = require("fs");
var import_path7 = require("path");

// src/direct-unity-client.ts
var net = __toESM(require("net"), 1);

// src/simple-framer.ts
var CONTENT_LENGTH_HEADER = "Content-Length:";
var HEADER_SEPARATOR = "\r\n\r\n";
function createFrame(jsonContent) {
  const contentLength = Buffer.byteLength(jsonContent, "utf8");
  return `${CONTENT_LENGTH_HEADER} ${contentLength}${HEADER_SEPARATOR}${jsonContent}`;
}
function parseFrameFromBuffer(data) {
  if (!data || data.length === 0) {
    return { contentLength: -1, headerLength: -1, isComplete: false };
  }
  const separatorBuffer = Buffer.from(HEADER_SEPARATOR, "utf8");
  const separatorIndex = data.indexOf(separatorBuffer);
  if (separatorIndex === -1) {
    return { contentLength: -1, headerLength: -1, isComplete: false };
  }
  const headerSection = data.subarray(0, separatorIndex).toString("utf8");
  const headerLength = separatorIndex + separatorBuffer.length;
  const contentLength = parseContentLength(headerSection);
  if (contentLength === -1) {
    return { contentLength: -1, headerLength: -1, isComplete: false };
  }
  const expectedTotalLength = headerLength + contentLength;
  const isComplete = data.length >= expectedTotalLength;
  return { contentLength, headerLength, isComplete };
}
function extractFrameFromBuffer(data, contentLength, headerLength) {
  if (!data || data.length === 0 || contentLength < 0 || headerLength < 0) {
    return { jsonContent: null, remainingData: data || Buffer.alloc(0) };
  }
  const expectedTotalLength = headerLength + contentLength;
  if (data.length < expectedTotalLength) {
    return { jsonContent: null, remainingData: data };
  }
  const jsonContent = data.subarray(headerLength, headerLength + contentLength).toString("utf8");
  const remainingData = data.subarray(expectedTotalLength);
  return { jsonContent, remainingData };
}
function parseContentLength(headerSection) {
  const lines = headerSection.split(/\r?\n/);
  for (const line of lines) {
    const trimmedLine = line.trim();
    const lowerLine = trimmedLine.toLowerCase();
    if (lowerLine.startsWith("content-length:")) {
      const colonIndex = trimmedLine.indexOf(":");
      if (colonIndex === -1 || colonIndex >= trimmedLine.length - 1) {
        continue;
      }
      const valueString = trimmedLine.substring(colonIndex + 1).trim();
      const parsedValue = parseInt(valueString, 10);
      if (isNaN(parsedValue) || parsedValue < 0) {
        return -1;
      }
      return parsedValue;
    }
  }
  return -1;
}

// src/direct-unity-client.ts
var JSONRPC_VERSION = "2.0";
var DEFAULT_HOST = "127.0.0.1";
var NETWORK_TIMEOUT_MS = 18e4;
var DirectUnityClient = class {
  constructor(port, host = DEFAULT_HOST) {
    this.port = port;
    this.host = host;
  }
  port;
  host;
  socket = null;
  requestId = 0;
  receiveBuffer = Buffer.alloc(0);
  async connect() {
    return new Promise((resolve8, reject) => {
      this.socket = new net.Socket();
      this.socket.on("error", (error) => {
        reject(new Error(`Connection error: ${error.message}`));
      });
      this.socket.connect(this.port, this.host, () => {
        resolve8();
      });
    });
  }
  async sendRequest(method, params, options) {
    if (!this.socket) {
      throw new Error("Not connected to Unity");
    }
    const request = {
      jsonrpc: JSONRPC_VERSION,
      method,
      params: params ?? {},
      id: ++this.requestId
    };
    if (options?.requestMetadata !== void 0) {
      request["x-uloop"] = options.requestMetadata;
    }
    const requestJson = JSON.stringify(request);
    const framedMessage = createFrame(requestJson);
    return new Promise((resolve8, reject) => {
      const socket = this.socket;
      const cleanup = () => {
        clearTimeout(timeoutId);
        socket.off("data", onData);
        socket.off("error", onError);
        socket.off("close", onClose);
      };
      const timeoutId = setTimeout(() => {
        cleanup();
        reject(
          new Error(
            `Request timed out after ${NETWORK_TIMEOUT_MS}ms. Unity may be frozen or busy. [For AI] Report this to the user and ask how to proceed.`
          )
        );
      }, NETWORK_TIMEOUT_MS);
      const onData = (chunk) => {
        this.receiveBuffer = Buffer.concat([this.receiveBuffer, chunk]);
        const parseResult = parseFrameFromBuffer(this.receiveBuffer);
        if (!parseResult.isComplete) {
          return;
        }
        const extractResult = extractFrameFromBuffer(
          this.receiveBuffer,
          parseResult.contentLength,
          parseResult.headerLength
        );
        if (extractResult.jsonContent === null) {
          return;
        }
        cleanup();
        this.receiveBuffer = extractResult.remainingData;
        const response = JSON.parse(extractResult.jsonContent);
        if (response.error) {
          const data = response.error.data;
          const dataMessage = data !== null && data !== void 0 && typeof data === "object" && "message" in data ? ` (${data.message})` : "";
          reject(new Error(`Unity error: ${response.error.message}${dataMessage}`));
          return;
        }
        resolve8(response.result);
      };
      const onError = (error) => {
        cleanup();
        reject(new Error(`Connection lost: ${error.message}`));
      };
      const onClose = () => {
        cleanup();
        reject(new Error("UNITY_NO_RESPONSE"));
      };
      socket.on("data", onData);
      socket.on("error", onError);
      socket.on("close", onClose);
      socket.write(framedMessage);
    });
  }
  disconnect() {
    if (this.socket) {
      this.socket.destroy();
      this.socket = null;
    }
    this.receiveBuffer = Buffer.alloc(0);
  }
  isConnected() {
    return this.socket !== null && !this.socket.destroyed;
  }
};

// src/port-resolver.ts
var import_promises = require("fs/promises");
var import_fs2 = require("fs");
var import_path2 = require("path");

// src/project-root.ts
var import_fs = require("fs");
var import_path = require("path");
var CHILD_SEARCH_MAX_DEPTH = 3;
var EXCLUDED_DIRS = /* @__PURE__ */ new Set([
  "node_modules",
  ".git",
  "Temp",
  "obj",
  "Build",
  "Builds",
  "Logs",
  "Library"
]);
function isUnityProject(dirPath) {
  const hasAssets = (0, import_fs.existsSync)((0, import_path.join)(dirPath, "Assets"));
  const hasProjectSettings = (0, import_fs.existsSync)((0, import_path.join)(dirPath, "ProjectSettings"));
  return hasAssets && hasProjectSettings;
}
function getUnitySettingsCandidatePaths(dirPath) {
  const settingsPath = (0, import_path.join)(dirPath, "UserSettings/UnityMcpSettings.json");
  return [settingsPath, `${settingsPath}.tmp`, `${settingsPath}.bak`];
}
function hasUloopInstalled(dirPath) {
  return getUnitySettingsCandidatePaths(dirPath).some((path) => (0, import_fs.existsSync)(path));
}
function isUnityProjectWithUloop(dirPath) {
  return isUnityProject(dirPath) && hasUloopInstalled(dirPath);
}
function findUnityProjectsInChildren(startPath, maxDepth) {
  const projects = [];
  function scan(currentPath, depth) {
    if (depth > maxDepth) {
      return;
    }
    if (!(0, import_fs.existsSync)(currentPath)) {
      return;
    }
    if (isUnityProjectWithUloop(currentPath)) {
      projects.push(currentPath);
      return;
    }
    let entries;
    try {
      entries = (0, import_fs.readdirSync)(currentPath, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      if (!entry.isDirectory()) {
        continue;
      }
      if (EXCLUDED_DIRS.has(entry.name)) {
        continue;
      }
      const fullPath = (0, import_path.join)(currentPath, entry.name);
      scan(fullPath, depth + 1);
    }
  }
  scan(startPath, 0);
  return projects.sort();
}
function findUnityProjectInParents(startPath) {
  let currentPath = startPath;
  while (true) {
    if (isUnityProjectWithUloop(currentPath)) {
      return currentPath;
    }
    const isGitRoot = (0, import_fs.existsSync)((0, import_path.join)(currentPath, ".git"));
    if (isGitRoot) {
      return null;
    }
    const parentPath = (0, import_path.dirname)(currentPath);
    if (parentPath === currentPath) {
      return null;
    }
    currentPath = parentPath;
  }
}
var hasWarnedMultipleProjects = false;
function findUnityProjectRoot(startPath = process.cwd()) {
  if (isUnityProjectWithUloop(startPath)) {
    return startPath;
  }
  const childProjects = findUnityProjectsInChildren(startPath, CHILD_SEARCH_MAX_DEPTH);
  if (childProjects.length > 0) {
    if (childProjects.length > 1 && !hasWarnedMultipleProjects) {
      hasWarnedMultipleProjects = true;
      console.error("\x1B[33mWarning: Multiple Unity projects found in child directories:\x1B[0m");
      for (const project of childProjects) {
        console.error(`  - ${project}`);
      }
      console.error(
        "\x1B[33mRun from a Unity project root or use --project-path to specify one.\x1B[0m"
      );
      console.error("");
    }
    return childProjects[0];
  }
  return findUnityProjectInParents(startPath);
}
function getUnityProjectStatus(startPath = process.cwd()) {
  const unityProjectWithUloop = findUnityProjectRoot(startPath);
  if (unityProjectWithUloop) {
    return { found: true, path: unityProjectWithUloop, hasUloop: true };
  }
  const unityProjectWithoutUloop = findUnityProjectWithoutUloop(startPath);
  if (unityProjectWithoutUloop) {
    return { found: true, path: unityProjectWithoutUloop, hasUloop: false };
  }
  return { found: false, path: null, hasUloop: false };
}
function findUnityProjectWithoutUloop(startPath) {
  const childProject = findUnityProjectInChildrenWithoutUloop(startPath, CHILD_SEARCH_MAX_DEPTH);
  if (childProject) {
    return childProject;
  }
  return findUnityProjectInParentsWithoutUloop(startPath);
}
function findUnityProjectInChildrenWithoutUloop(startPath, maxDepth) {
  function scan(currentPath, depth) {
    if (depth > maxDepth || !(0, import_fs.existsSync)(currentPath)) {
      return null;
    }
    if (isUnityProject(currentPath)) {
      return currentPath;
    }
    let entries;
    try {
      entries = (0, import_fs.readdirSync)(currentPath, { withFileTypes: true });
    } catch {
      return null;
    }
    for (const entry of entries) {
      if (!entry.isDirectory() || EXCLUDED_DIRS.has(entry.name)) {
        continue;
      }
      const result = scan((0, import_path.join)(currentPath, entry.name), depth + 1);
      if (result) {
        return result;
      }
    }
    return null;
  }
  return scan(startPath, 0);
}
function findUnityProjectInParentsWithoutUloop(startPath) {
  let currentPath = startPath;
  while (true) {
    if (isUnityProject(currentPath)) {
      return currentPath;
    }
    const isGitRoot = (0, import_fs.existsSync)((0, import_path.join)(currentPath, ".git"));
    if (isGitRoot) {
      return null;
    }
    const parentPath = (0, import_path.dirname)(currentPath);
    if (parentPath === currentPath) {
      return null;
    }
    currentPath = parentPath;
  }
}

// src/port-resolver.ts
var UnityNotRunningError = class extends Error {
  constructor(projectRoot) {
    super("UNITY_NOT_RUNNING");
    this.projectRoot = projectRoot;
  }
  projectRoot;
};
var UnityServerNotRunningError = class extends Error {
  constructor(projectRoot) {
    super("UNITY_SERVER_NOT_RUNNING");
    this.projectRoot = projectRoot;
  }
  projectRoot;
};
function normalizePort(port) {
  if (typeof port !== "number") {
    return null;
  }
  if (!Number.isInteger(port)) {
    return null;
  }
  if (port < 1 || port > 65535) {
    return null;
  }
  return port;
}
function resolvePortFromUnitySettings(settings) {
  const customPort = normalizePort(settings.customPort);
  if (customPort !== null) {
    return customPort;
  }
  return null;
}
function validateProjectPath(projectPath) {
  const resolved = (0, import_path2.resolve)(projectPath);
  if (!(0, import_fs2.existsSync)(resolved)) {
    throw new Error(`Path does not exist: ${resolved}`);
  }
  if (!isUnityProject(resolved)) {
    throw new Error(`Not a Unity project (Assets/ or ProjectSettings/ not found): ${resolved}`);
  }
  if (!hasUloopInstalled(resolved)) {
    throw new Error(
      `${PRODUCT_DISPLAY_NAME} is not installed in this project (UserSettings/UnityMcpSettings.json not found): ${resolved}`
    );
  }
  return resolved;
}
function normalizeProjectRootPath(projectRoot) {
  return projectRoot.replace(/\/+$/, "");
}
function createSettingsReadError(projectRoot) {
  const settingsPath = (0, import_path2.join)(projectRoot, "UserSettings/UnityMcpSettings.json");
  return new Error(
    `Could not read Unity server port from settings.

  Settings file: ${settingsPath}

Run 'uloop launch -r' to restart Unity.`
  );
}
async function readUnitySettingsOrThrow(projectRoot) {
  for (const settingsPath of getUnitySettingsCandidatePaths(projectRoot)) {
    let content;
    try {
      content = await (0, import_promises.readFile)(settingsPath, "utf-8");
    } catch {
      continue;
    }
    let parsed;
    try {
      parsed = JSON.parse(content);
    } catch {
      continue;
    }
    if (typeof parsed !== "object" || parsed === null) {
      continue;
    }
    return parsed;
  }
  throw createSettingsReadError(projectRoot);
}
function resolvePortFromSettingsOrThrow(settings, projectRoot) {
  const port = resolvePortFromUnitySettings(settings);
  if (port !== null) {
    return port;
  }
  throw createSettingsReadError(projectRoot);
}
function tryCreateRequestMetadata(settings, projectRoot) {
  if (typeof settings.projectRootPath !== "string" || settings.projectRootPath.length === 0 || typeof settings.serverSessionId !== "string" || settings.serverSessionId.length === 0) {
    return null;
  }
  const normalizedProjectRoot = normalizeProjectRootPath(projectRoot);
  const normalizedSettingsProjectRoot = normalizeProjectRootPath(settings.projectRootPath);
  if (normalizedProjectRoot !== normalizedSettingsProjectRoot) {
    return null;
  }
  return {
    expectedProjectRoot: normalizedProjectRoot,
    expectedServerSessionId: settings.serverSessionId
  };
}
async function resolveUnityConnection(explicitPort, projectPath) {
  if (explicitPort !== void 0 && projectPath !== void 0) {
    throw new Error("Cannot specify both --port and --project-path. Use one or the other.");
  }
  if (explicitPort !== void 0) {
    return {
      port: explicitPort,
      projectRoot: null,
      requestMetadata: null,
      shouldValidateProject: false
    };
  }
  let projectRoot;
  if (projectPath !== void 0) {
    projectRoot = validateProjectPath(projectPath);
  } else {
    projectRoot = findUnityProjectRoot();
    if (projectRoot === null) {
      throw new Error("Unity project not found. Use --project-path option to specify the target.");
    }
  }
  const settings = await readUnitySettingsOrThrow(projectRoot);
  const port = resolvePortFromSettingsOrThrow(settings, projectRoot);
  const requestMetadata = tryCreateRequestMetadata(settings, projectRoot);
  return {
    port,
    projectRoot,
    requestMetadata,
    shouldValidateProject: requestMetadata === null
  };
}

// src/project-validator.ts
var import_node_assert = __toESM(require("node:assert"), 1);
var import_promises2 = require("fs/promises");
var import_path3 = require("path");
var ProjectMismatchError = class extends Error {
  constructor(expectedProjectRoot, connectedProjectRoot) {
    super("PROJECT_MISMATCH");
    this.expectedProjectRoot = expectedProjectRoot;
    this.connectedProjectRoot = connectedProjectRoot;
  }
  expectedProjectRoot;
  connectedProjectRoot;
};
var JSON_RPC_METHOD_NOT_FOUND = -32601;
async function normalizePath(path) {
  const resolved = await (0, import_promises2.realpath)(path);
  return resolved.replace(/\/+$/, "");
}
async function validateConnectedProject(client, expectedProjectRoot) {
  (0, import_node_assert.default)(client.isConnected(), "client must be connected before validation");
  let response;
  try {
    response = await client.sendRequest("get-version", {});
  } catch (error) {
    if (error instanceof Error && (error.message.includes(`${JSON_RPC_METHOD_NOT_FOUND}`) || /method not found/i.test(error.message) || /unknown tool/i.test(error.message))) {
      console.error(
        `Warning: Could not verify project identity (get-version not available). Consider updating ${PRODUCT_DISPLAY_NAME} package.`
      );
      return;
    }
    throw error;
  }
  if (typeof response?.DataPath !== "string" || response.DataPath.length === 0) {
    console.error("Warning: Could not verify project identity (invalid get-version response).");
    return;
  }
  const connectedProjectRoot = (0, import_path3.dirname)(response.DataPath);
  const normalizedExpected = await normalizePath(expectedProjectRoot);
  const normalizedConnected = await normalizePath(connectedProjectRoot);
  if (normalizedExpected !== normalizedConnected) {
    throw new ProjectMismatchError(normalizedExpected, normalizedConnected);
  }
}

// src/tool-cache.ts
var import_fs3 = require("fs");
var import_path4 = require("path");

// src/default-tools.json
var default_tools_default = {
  version: "3.0.0-beta.0",
  tools: [
    {
      name: "compile",
      description: "Execute Unity project compilation",
      inputSchema: {
        type: "object",
        properties: {
          ForceRecompile: {
            type: "boolean",
            description: "Force full recompilation"
          },
          WaitForDomainReload: {
            type: "boolean",
            description: "Wait for domain reload completion before returning"
          }
        }
      }
    },
    {
      name: "get-logs",
      description: "Retrieve logs from Unity Console",
      inputSchema: {
        type: "object",
        properties: {
          LogType: {
            type: "string",
            description: "Log type filter",
            enum: [
              "Error",
              "Warning",
              "Log",
              "All"
            ],
            default: "All"
          },
          MaxCount: {
            type: "integer",
            description: "Maximum number of logs to retrieve",
            default: 100
          },
          SearchText: {
            type: "string",
            description: "Text to search within logs"
          },
          IncludeStackTrace: {
            type: "boolean",
            description: "Include stack trace in output",
            default: false
          },
          UseRegex: {
            type: "boolean",
            description: "Use regex for search"
          },
          SearchInStackTrace: {
            type: "boolean",
            description: "Search within stack trace"
          }
        }
      }
    },
    {
      name: "run-tests",
      description: "Execute Unity Test Runner",
      inputSchema: {
        type: "object",
        properties: {
          TestMode: {
            type: "string",
            description: "Test mode",
            enum: [
              "EditMode",
              "PlayMode"
            ],
            default: "EditMode"
          },
          FilterType: {
            type: "string",
            description: "Filter type",
            enum: [
              "all",
              "exact",
              "regex",
              "assembly"
            ],
            default: "all"
          },
          FilterValue: {
            type: "string",
            description: "Filter value"
          },
          SaveBeforeRun: {
            type: "boolean",
            description: "Save unsaved loaded Scene changes and current Prefab Stage changes before running tests",
            default: false
          }
        }
      }
    },
    {
      name: "clear-console",
      description: "Clear Unity console logs",
      inputSchema: {
        type: "object",
        properties: {
          AddConfirmationMessage: {
            type: "boolean",
            description: "Add confirmation message after clearing"
          }
        }
      }
    },
    {
      name: "focus-window",
      description: "Bring Unity Editor window to front",
      inputSchema: {
        type: "object",
        properties: {}
      }
    },
    {
      name: "get-hierarchy",
      description: "Get Unity Hierarchy structure",
      inputSchema: {
        type: "object",
        properties: {
          RootPath: {
            type: "string",
            description: "Root GameObject path"
          },
          MaxDepth: {
            type: "integer",
            description: "Maximum depth (-1 for unlimited)",
            default: -1
          },
          IncludeComponents: {
            type: "boolean",
            description: "Include component information",
            default: true
          },
          IncludeInactive: {
            type: "boolean",
            description: "Include inactive GameObjects",
            default: true
          },
          IncludePaths: {
            type: "boolean",
            description: "Include path information"
          },
          UseComponentsLut: {
            type: "string",
            description: "Use LUT for components (auto|true|false)",
            default: "auto"
          },
          UseSelection: {
            type: "boolean",
            description: "Use selected GameObject(s) as root(s). When true, RootPath is ignored.",
            default: false
          }
        }
      }
    },
    {
      name: "find-game-objects",
      description: "Find GameObjects with search criteria",
      inputSchema: {
        type: "object",
        properties: {
          NamePattern: {
            type: "string",
            description: "Name pattern to search"
          },
          SearchMode: {
            type: "string",
            description: "Search mode",
            enum: [
              "Exact",
              "Path",
              "Regex",
              "Contains",
              "Selected"
            ],
            default: "Exact"
          },
          RequiredComponents: {
            type: "array",
            description: "Required components",
            items: {
              type: "string"
            }
          },
          Tag: {
            type: "string",
            description: "Tag filter"
          },
          Layer: {
            type: "integer",
            description: "Layer filter"
          },
          MaxResults: {
            type: "integer",
            description: "Maximum number of results",
            default: 20
          },
          IncludeInactive: {
            type: "boolean",
            description: "Include inactive GameObjects"
          },
          IncludeInheritedProperties: {
            type: "boolean",
            description: "Include inherited properties"
          }
        }
      }
    },
    {
      name: "screenshot",
      description: "Take a screenshot of Unity EditorWindow and save as PNG",
      inputSchema: {
        type: "object",
        properties: {
          WindowName: {
            type: "string",
            description: "Window name to capture (e.g., 'Game', 'Scene', 'Console', 'Inspector', 'Project', 'Hierarchy')",
            default: "Game"
          },
          ResolutionScale: {
            type: "number",
            description: "Resolution scale (0.1 to 1.0)",
            default: 1
          },
          MatchMode: {
            type: "string",
            description: "Window name matching mode (all case-insensitive)",
            enum: [
              "exact",
              "prefix",
              "contains"
            ],
            default: "exact"
          },
          OutputDirectory: {
            type: "string",
            description: "Output directory path for saving screenshots. When empty, uses default path (.uloop/outputs/Screenshots/). Accepts absolute paths.",
            default: ""
          },
          CaptureMode: {
            type: "string",
            description: "Capture mode: window=capture EditorWindow including toolbar, rendering=capture game rendering only (PlayMode required). Response includes CoordinateSystem ('gameView' or 'window'), ResolutionScale, and YOffset. For gameView: sim_x = image_x / ResolutionScale, sim_y = image_y / ResolutionScale + YOffset.",
            enum: [
              "window",
              "rendering"
            ],
            default: "window"
          },
          AnnotateElements: {
            type: "boolean",
            description: "Annotate interactive UI elements with index labels (A, B, C...) on the screenshot. Only works with CaptureMode=rendering in PlayMode. Response includes AnnotatedElements array with element metadata sorted by z-order.",
            default: false
          },
          ElementsOnly: {
            type: "boolean",
            description: "Return only annotated element JSON without capturing a screenshot image. Requires AnnotateElements=true and CaptureMode=rendering in PlayMode.",
            default: false
          }
        }
      }
    },
    {
      name: "execute-dynamic-code",
      description: "Execute C# code in Unity Editor",
      inputSchema: {
        type: "object",
        properties: {
          Code: {
            type: "string",
            description: "C# code to execute"
          },
          Parameters: {
            type: "object",
            description: "Runtime parameters for execution"
          },
          CompileOnly: {
            type: "boolean",
            description: "Compile only without execution"
          },
          YieldToForegroundRequests: {
            type: "boolean",
            description: "Allow foreground requests to preempt this execution"
          }
        }
      }
    },
    {
      name: "get-project-info",
      description: "Get Unity project information",
      inputSchema: {
        type: "object",
        properties: {}
      }
    },
    {
      name: "get-version",
      description: "Get Unity CLI Loop version information",
      inputSchema: {
        type: "object",
        properties: {}
      }
    },
    {
      name: "control-play-mode",
      description: "Control Unity Editor play mode (play/stop/pause)",
      inputSchema: {
        type: "object",
        properties: {
          Action: {
            type: "string",
            description: "Action to perform: Play - Start play mode, Stop - Stop play mode, Pause - Pause play mode",
            enum: [
              "Play",
              "Stop",
              "Pause"
            ],
            default: "Play"
          }
        }
      }
    },
    {
      name: "simulate-mouse-ui",
      description: "Simulate mouse click, long-press, and drag on PlayMode UI elements via EventSystem screen coordinates",
      inputSchema: {
        type: "object",
        properties: {
          Action: {
            type: "string",
            description: "Mouse action: Click - click at position, Drag - one-shot drag, DragStart - begin drag and hold, DragMove - move while holding drag, DragEnd - release drag, LongPress - press and hold for Duration seconds",
            enum: [
              "Click",
              "Drag",
              "DragStart",
              "DragMove",
              "DragEnd",
              "LongPress"
            ],
            default: "Click"
          },
          X: {
            type: "number",
            description: "Target X position in screen pixels (origin: top-left). For Drag action, this is the destination.",
            default: 0
          },
          Y: {
            type: "number",
            description: "Target Y position in screen pixels (origin: top-left). For Drag action, this is the destination.",
            default: 0
          },
          FromX: {
            type: "number",
            description: "Start X position for Drag action (origin: top-left). Drag starts here and moves to X,Y.",
            default: 0
          },
          FromY: {
            type: "number",
            description: "Start Y position for Drag action (origin: top-left). Drag starts here and moves to X,Y.",
            default: 0
          },
          DragSpeed: {
            type: "number",
            description: "Drag speed in pixels per second (0 for instant). Applies to Drag, DragMove, and DragEnd actions.",
            default: 2e3
          },
          Duration: {
            type: "number",
            description: "Hold duration in seconds for LongPress action.",
            default: 0.5
          },
          Button: {
            type: "string",
            description: "Mouse button: Left (default), Right, Middle.",
            enum: [
              "Left",
              "Right",
              "Middle"
            ],
            default: "Left"
          },
          BypassRaycast: {
            type: "boolean",
            description: "Bypass EventSystem raycast and send click, long-press, or drag events directly to TargetPath, or DropTargetPath for DragEnd. Useful for interacting with UI behind a raycast-blocking overlay.",
            default: false
          },
          TargetPath: {
            type: "string",
            description: "Hierarchy path of the target GameObject used by Click, LongPress, Drag, and DragStart when BypassRaycast is true, for example Canvas/Panel/Button.",
            default: ""
          },
          DropTargetPath: {
            type: "string",
            description: "Optional hierarchy path of the drop target used by Drag and DragEnd, for example Canvas/DropZone.",
            default: ""
          }
        }
      }
    },
    {
      name: "simulate-mouse-input",
      description: "Simulate mouse input in PlayMode via Input System. Injects button clicks, mouse delta, and scroll wheel directly into Mouse.current for game logic that reads Input System. Requires the Input System package and Active Input Handling set to 'Input System Package (New)' or 'Both'.",
      inputSchema: {
        type: "object",
        properties: {
          Action: {
            type: "string",
            description: "Mouse input action: Click - inject button press+release, LongPress - inject button hold for Duration seconds, MoveDelta - inject mouse delta (one-shot), SmoothDelta - inject mouse delta smoothly over Duration seconds, Scroll - inject scroll wheel",
            enum: [
              "Click",
              "LongPress",
              "MoveDelta",
              "SmoothDelta",
              "Scroll"
            ],
            default: "Click"
          },
          X: {
            type: "number",
            description: "Target X position in screen pixels (origin: top-left). Used by Click and LongPress.",
            default: 0
          },
          Y: {
            type: "number",
            description: "Target Y position in screen pixels (origin: top-left). Used by Click and LongPress.",
            default: 0
          },
          Button: {
            type: "string",
            description: "Mouse button: Left (default), Right, Middle. Used by Click and LongPress.",
            enum: [
              "Left",
              "Right",
              "Middle"
            ],
            default: "Left"
          },
          Duration: {
            type: "number",
            description: "Duration in seconds for LongPress hold, SmoothDelta interpolation, or minimum hold time for Click (0 = one-shot tap).",
            default: 0
          },
          DeltaX: {
            type: "number",
            description: "Delta X in pixels for MoveDelta/SmoothDelta action. Positive = right.",
            default: 0
          },
          DeltaY: {
            type: "number",
            description: "Delta Y in pixels for MoveDelta/SmoothDelta action. Positive = up.",
            default: 0
          },
          ScrollX: {
            type: "number",
            description: "Horizontal scroll delta for Scroll action.",
            default: 0
          },
          ScrollY: {
            type: "number",
            description: "Vertical scroll delta for Scroll action. Positive = up, negative = down. Typically 120 per notch.",
            default: 0
          }
        }
      }
    },
    {
      name: "simulate-keyboard",
      description: "Simulate keyboard key input in PlayMode via Input System. Supports one-shot press, key-down hold, and key-up release for game controls (WASD, Space, etc.). Requires the Input System package (com.unity.inputsystem).",
      inputSchema: {
        type: "object",
        properties: {
          Action: {
            type: "string",
            description: "Keyboard action: Press - one-shot key tap (Down then Up), KeyDown - hold key down, KeyUp - release held key",
            enum: [
              "Press",
              "KeyDown",
              "KeyUp"
            ],
            default: "Press"
          },
          Key: {
            type: "string",
            description: 'Key name matching Input System Key enum (e.g. "W", "Space", "LeftShift", "A", "Return"). Case-insensitive.'
          },
          Duration: {
            type: "number",
            description: "Hold duration in seconds for Press action (0 = one-shot tap). Ignored by KeyDown/KeyUp.",
            default: 0
          }
        }
      }
    },
    {
      name: "record-input",
      description: "Record keyboard and mouse input during PlayMode. Captures key presses, mouse clicks, mouse delta, and scroll events frame-by-frame. Saves to JSON for later replay.",
      inputSchema: {
        type: "object",
        properties: {
          Action: {
            type: "string",
            enum: [
              "Start",
              "Stop"
            ],
            description: "Recording action: Start - begin recording input, Stop - stop recording and save to file",
            default: "Start"
          },
          OutputPath: {
            type: "string",
            description: "Output file path for the recording JSON. If empty, auto-generates under .uloop/outputs/InputRecordings/",
            default: ""
          },
          Keys: {
            type: "string",
            description: "Comma-separated key filter. Only record specified keys (e.g. 'W,A,S,D,Space'). Empty means record all common game keys.",
            default: ""
          },
          DelaySeconds: {
            type: "number",
            description: "Countdown delay in seconds before recording starts (0-10). Gives time to switch focus to Game View.",
            default: 3
          },
          ShowOverlay: {
            type: "boolean",
            description: "Show recording overlay (countdown + REC indicator)",
            default: true
          }
        }
      }
    },
    {
      name: "replay-input",
      description: "Replay recorded keyboard and mouse input during PlayMode. Injects recorded events frame-by-frame via Input System to reproduce exact input sequences.",
      inputSchema: {
        type: "object",
        properties: {
          Action: {
            type: "string",
            enum: [
              "Start",
              "Stop",
              "Status"
            ],
            description: "Replay action: Start - begin replaying, Stop - stop mid-way, Status - check progress",
            default: "Start"
          },
          InputPath: {
            type: "string",
            description: "Path to recording JSON file. If empty, auto-detects the latest recording in .uloop/outputs/InputRecordings/",
            default: ""
          },
          ShowOverlay: {
            type: "boolean",
            description: "Show visualization overlay during replay",
            default: true
          },
          Loop: {
            type: "boolean",
            description: "Loop replay continuously",
            default: false
          }
        }
      }
    }
  ]
};

// src/tool-cache.ts
var CACHE_DIR = ".uloop";
var CACHE_FILE = "tools.json";
function getCacheDir(projectPath) {
  if (projectPath !== void 0) {
    return (0, import_path4.join)(projectPath, CACHE_DIR);
  }
  const projectRoot = findUnityProjectRoot();
  if (projectRoot === null) {
    return (0, import_path4.join)(process.cwd(), CACHE_DIR);
  }
  return (0, import_path4.join)(projectRoot, CACHE_DIR);
}
function getCachePath(projectPath) {
  return (0, import_path4.join)(getCacheDir(projectPath), CACHE_FILE);
}
function getDefaultTools() {
  return default_tools_default;
}
function loadToolsCache(projectPath) {
  const cachePath = getCachePath(projectPath);
  if ((0, import_fs3.existsSync)(cachePath)) {
    try {
      const content = (0, import_fs3.readFileSync)(cachePath, "utf-8");
      return JSON.parse(content);
    } catch {
      return getDefaultTools();
    }
  }
  return getDefaultTools();
}
function saveToolsCache(cache) {
  const cacheDir = getCacheDir();
  const cachePath = getCachePath();
  if (!(0, import_fs3.existsSync)(cacheDir)) {
    (0, import_fs3.mkdirSync)(cacheDir, { recursive: true });
  }
  const content = JSON.stringify(cache, null, 2);
  (0, import_fs3.writeFileSync)(cachePath, content, "utf-8");
}
function hasCacheFile() {
  return (0, import_fs3.existsSync)(getCachePath());
}
function getCacheFilePath() {
  return getCachePath();
}
function getDefaultToolNames() {
  const defaultTools = getDefaultTools();
  return new Set(defaultTools.tools.map((tool) => tool.name));
}
function getCachedServerVersion() {
  const cachePath = getCachePath();
  if (!(0, import_fs3.existsSync)(cachePath)) {
    return void 0;
  }
  try {
    const content = (0, import_fs3.readFileSync)(cachePath, "utf-8");
    const cache = JSON.parse(content);
    return typeof cache.serverVersion === "string" ? cache.serverVersion : void 0;
  } catch {
    return void 0;
  }
}

// src/tool-settings-loader.ts
var import_fs4 = require("fs");
var import_path5 = require("path");
var ULOOP_DIR = ".uloop";
var TOOL_SETTINGS_FILE = "settings.tools.json";
function loadDisabledTools(projectPath) {
  const projectRoot = projectPath !== void 0 ? (0, import_path5.resolve)(projectPath) : findUnityProjectRoot();
  if (projectRoot === null) {
    return [];
  }
  const settingsPath = (0, import_path5.join)(projectRoot, ULOOP_DIR, TOOL_SETTINGS_FILE);
  let content;
  try {
    content = (0, import_fs4.readFileSync)(settingsPath, "utf-8");
  } catch {
    return [];
  }
  if (!content.trim()) {
    return [];
  }
  let parsed;
  try {
    parsed = JSON.parse(content);
  } catch {
    return [];
  }
  if (typeof parsed !== "object" || parsed === null) {
    return [];
  }
  const data = parsed;
  if (!Array.isArray(data.disabledTools)) {
    return [];
  }
  return data.disabledTools;
}
function isToolEnabled(toolName, projectPath) {
  const disabledTools = loadDisabledTools(projectPath);
  return !disabledTools.includes(toolName);
}
function filterEnabledTools(tools, projectPath) {
  const disabledTools = loadDisabledTools(projectPath);
  if (disabledTools.length === 0) {
    return tools;
  }
  return tools.filter((tool) => !disabledTools.includes(tool.name));
}

// src/version.ts
var VERSION = "3.0.0-beta.0";

// src/spinner.ts
var SPINNER_FRAMES = ["\u280B", "\u2819", "\u2839", "\u2838", "\u283C", "\u2834", "\u2826", "\u2827", "\u2807", "\u280F"];
var FRAME_INTERVAL_MS = 80;
function resolveSpinnerStream(preference) {
  if (preference === "stdout") {
    if (process.stdout.isTTY) {
      return process.stdout;
    }
    if (process.stderr.isTTY) {
      return process.stderr;
    }
    return null;
  }
  if (preference === "stderr") {
    if (process.stderr.isTTY) {
      return process.stderr;
    }
    if (process.stdout.isTTY) {
      return process.stdout;
    }
    return null;
  }
  if (process.stderr.isTTY) {
    return process.stderr;
  }
  if (process.stdout.isTTY) {
    return process.stdout;
  }
  return null;
}
function createSpinner(initialMessage, preference = "auto") {
  const outputStream = resolveSpinnerStream(preference);
  if (outputStream === null) {
    return {
      update: () => {
      },
      stop: () => {
      }
    };
  }
  let frameIndex = 0;
  let currentMessage = initialMessage;
  const render = () => {
    const frame = SPINNER_FRAMES[frameIndex];
    outputStream.write(`\r\x1B[K${frame} ${currentMessage}`);
    frameIndex = (frameIndex + 1) % SPINNER_FRAMES.length;
  };
  render();
  const intervalId = setInterval(render, FRAME_INTERVAL_MS);
  return {
    update(message) {
      currentMessage = message;
      render();
    },
    stop() {
      clearInterval(intervalId);
      outputStream.write("\r\x1B[K");
    }
  };
}

// src/process-timing.ts
var CLI_PROCESS_STARTED_AT_MS = Date.now();
function getCliProcessAgeMilliseconds() {
  return Date.now() - CLI_PROCESS_STARTED_AT_MS;
}

// src/request-metadata.ts
var RETRYABLE_FAST_PROJECT_VALIDATION_ERROR_SUBSTRINGS = [
  "Fast project validation is unavailable.",
  "Unity CLI Loop server session changed."
];
function isRetryableFastProjectValidationErrorMessage(message) {
  return RETRYABLE_FAST_PROJECT_VALIDATION_ERROR_SUBSTRINGS.some(
    (substring) => message.includes(substring)
  );
}

// src/unity-process.ts
var import_node_child_process = require("node:child_process");
var import_node_util = require("node:util");
var execFileAsync = (0, import_node_util.promisify)(import_node_child_process.execFile);
var WINDOWS_PROCESS_QUERY = `Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" | Select-Object ProcessId, CommandLine | ConvertTo-Json -Compress`;
var defaultDependencies = {
  platform: process.platform,
  runCommand: runUnityProcessQuery
};
function buildUnityProcessCommand(platform) {
  if (platform === "darwin") {
    return {
      command: "ps",
      args: ["-Ao", "pid=,command="]
    };
  }
  if (platform === "linux") {
    return {
      command: "ps",
      args: ["-eo", "pid=,args="]
    };
  }
  if (platform === "win32") {
    return {
      command: "powershell.exe",
      args: ["-NoProfile", "-NonInteractive", "-Command", WINDOWS_PROCESS_QUERY]
    };
  }
  return null;
}
function parseUnityProcesses(platform, output) {
  if (platform === "win32") {
    return parseWindowsUnityProcesses(output);
  }
  return parsePsUnityProcesses(output);
}
function tokenizeCommandLine(commandLine) {
  const tokens = [];
  let current = "";
  let inQuotes = false;
  for (let i = 0; i < commandLine.length; i++) {
    const character = commandLine[i];
    if (character === '"') {
      inQuotes = !inQuotes;
      continue;
    }
    if (!inQuotes && /\s/.test(character)) {
      if (current.length > 0) {
        tokens.push(current);
        current = "";
      }
      continue;
    }
    current += character;
  }
  if (current.length > 0) {
    tokens.push(current);
  }
  return tokens;
}
function extractUnityProjectPath(commandLine) {
  const tokens = tokenizeCommandLine(commandLine);
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i].toLowerCase();
    if (token !== "-projectpath") {
      continue;
    }
    const projectPath = tokens[i + 1];
    return projectPath ?? null;
  }
  return null;
}
function normalizeUnityProjectPath(projectPath, platform) {
  const normalizedSeparators = projectPath.replace(/\\/g, "/").replace(/\/+$/, "");
  if (platform === "win32") {
    return normalizedSeparators.toLowerCase();
  }
  return normalizedSeparators;
}
function isUnityProcessForProject(commandLine, projectRoot, platform) {
  if (platform !== "win32") {
    return commandLineContainsProjectRoot(commandLine, projectRoot, platform);
  }
  const extractedProjectPath = extractUnityProjectPath(commandLine);
  if (extractedProjectPath === null) {
    return false;
  }
  return normalizeUnityProjectPath(extractedProjectPath, platform) === normalizeUnityProjectPath(projectRoot, platform);
}
function commandLineContainsProjectRoot(commandLine, projectRoot, platform) {
  const projectPathFlagIndex = commandLine.toLowerCase().indexOf(" -projectpath");
  if (projectPathFlagIndex === -1) {
    return false;
  }
  const normalizedProjectRoot = normalizeUnityProjectPath(projectRoot, platform);
  let projectRootIndex = commandLine.indexOf(normalizedProjectRoot, projectPathFlagIndex);
  while (projectRootIndex !== -1) {
    const beforeProjectRoot = commandLine[projectRootIndex - 1];
    const projectPathEndIndex = skipTrailingProjectPathSeparators(
      commandLine,
      projectRootIndex + normalizedProjectRoot.length
    );
    if (isProjectPathBoundaryCharacter(beforeProjectRoot) && isProjectPathTerminator(commandLine, projectPathEndIndex)) {
      return true;
    }
    projectRootIndex = commandLine.indexOf(normalizedProjectRoot, projectRootIndex + 1);
  }
  return false;
}
function isProjectPathBoundaryCharacter(character) {
  return character === void 0 || /\s|["']/.test(character);
}
function skipTrailingProjectPathSeparators(commandLine, startIndex) {
  let index = startIndex;
  while (readCharacterAt(commandLine, index) === "/") {
    index += 1;
  }
  return index;
}
function isProjectPathTerminator(commandLine, projectRootEndIndex) {
  const character = readCharacterAt(commandLine, projectRootEndIndex);
  if (character === null) {
    return true;
  }
  if (character === '"' || character === "'") {
    return true;
  }
  if (!/\s/.test(character)) {
    return false;
  }
  for (let i = projectRootEndIndex; i < commandLine.length; i++) {
    const trailingCharacter = readCharacterAt(commandLine, i);
    if (trailingCharacter === null) {
      return true;
    }
    if (/\s/.test(trailingCharacter)) {
      continue;
    }
    return trailingCharacter === "-";
  }
  return true;
}
function readCharacterAt(value, index) {
  const character = value.slice(index, index + 1);
  if (character.length === 0) {
    return null;
  }
  return character;
}
function isUnityEditorProcess(commandLine, platform) {
  const lowerCommandLine = commandLine.toLowerCase();
  if (lowerCommandLine.length === 0) {
    return false;
  }
  const projectPathFlagIndex = lowerCommandLine.indexOf(" -projectpath");
  const executableSection = projectPathFlagIndex === -1 ? lowerCommandLine : lowerCommandLine.slice(0, projectPathFlagIndex);
  if (platform === "win32") {
    return executableSection.includes("unity.exe");
  }
  if (platform === "darwin") {
    return executableSection.includes("/unity.app/contents/macos/unity");
  }
  if (platform === "linux") {
    return executableSection.endsWith("/unity") || executableSection.endsWith("/unity-editor") || executableSection.includes("/editor/unity");
  }
  return false;
}
async function findRunningUnityProcessForProject(projectRoot, dependencies = defaultDependencies) {
  const unityProcessCommand = buildUnityProcessCommand(dependencies.platform);
  if (unityProcessCommand === null) {
    return null;
  }
  const output = await dependencies.runCommand(
    unityProcessCommand.command,
    unityProcessCommand.args
  );
  const runningProcesses = parseUnityProcesses(dependencies.platform, output);
  const matchingProcess = runningProcesses.find(
    (processInfo) => isUnityEditorProcess(processInfo.commandLine, dependencies.platform) && isUnityProcessForProject(processInfo.commandLine, projectRoot, dependencies.platform)
  );
  if (matchingProcess === void 0) {
    return null;
  }
  return {
    pid: matchingProcess.pid
  };
}
async function runUnityProcessQuery(command, args) {
  const { stdout } = await execFileAsync(command, args, {
    encoding: "utf8",
    maxBuffer: 1024 * 1024
  });
  return stdout;
}
function parsePsUnityProcesses(output) {
  return output.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0).map((line) => {
    const match = line.match(/^(\d+)\s+(.+)$/);
    if (match === null) {
      return null;
    }
    return {
      pid: Number.parseInt(match[1], 10),
      commandLine: match[2]
    };
  }).filter((processInfo) => processInfo !== null);
}
function parseWindowsUnityProcesses(output) {
  const trimmed = output.trim();
  if (trimmed.length === 0) {
    return [];
  }
  const parsed = JSON.parse(trimmed);
  const processArray = Array.isArray(parsed) ? parsed : [parsed];
  return processArray.filter(isWindowsUnityProcessWithCommandLine).map((processInfo) => ({
    pid: processInfo.ProcessId,
    commandLine: processInfo.CommandLine
  }));
}
function isWindowsUnityProcessWithCommandLine(processInfo) {
  return typeof processInfo.ProcessId === "number" && typeof processInfo.CommandLine === "string";
}

// src/compile-helpers.ts
var import_node_assert2 = __toESM(require("node:assert"), 1);
var import_fs5 = require("fs");
var net2 = __toESM(require("net"), 1);
var import_path6 = require("path");
var SAFE_REQUEST_ID_PATTERN = /^[a-zA-Z0-9_-]+$/;
var COMPILE_FORCE_RECOMPILE_ARG_KEYS = [
  "ForceRecompile",
  "forceRecompile",
  "force_recompile",
  "force-recompile"
];
var COMPILE_WAIT_FOR_DOMAIN_RELOAD_ARG_KEYS = [
  "WaitForDomainReload",
  "waitForDomainReload",
  "wait_for_domain_reload",
  "wait-for-domain-reload"
];
var LOCK_GRACE_PERIOD_MS = 500;
var READINESS_CHECK_TIMEOUT_MS = 3e3;
var DEFAULT_HOST2 = "127.0.0.1";
var CONTENT_LENGTH_HEADER2 = "Content-Length:";
var HEADER_SEPARATOR2 = "\r\n\r\n";
function toBoolean(value) {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") {
    return value.toLowerCase() === "true";
  }
  return false;
}
function getCompileBooleanArg(args, keys) {
  for (const key of keys) {
    if (!(key in args)) {
      continue;
    }
    return toBoolean(args[key]);
  }
  return false;
}
function resolveCompileExecutionOptions(args) {
  return {
    forceRecompile: getCompileBooleanArg(args, COMPILE_FORCE_RECOMPILE_ARG_KEYS),
    waitForDomainReload: getCompileBooleanArg(args, COMPILE_WAIT_FOR_DOMAIN_RELOAD_ARG_KEYS)
  };
}
function createCompileRequestId() {
  const timestamp = Date.now();
  const randomToken = Math.floor(Math.random() * 1e6).toString().padStart(6, "0");
  return `compile_${timestamp}_${randomToken}`;
}
function ensureCompileRequestId(args) {
  const existingRequestId = args["RequestId"];
  if (typeof existingRequestId === "string" && existingRequestId.length > 0) {
    if (SAFE_REQUEST_ID_PATTERN.test(existingRequestId)) {
      return existingRequestId;
    }
  }
  const requestId = createCompileRequestId();
  args["RequestId"] = requestId;
  return requestId;
}
function getCompileResultFilePath(projectRoot, requestId) {
  (0, import_node_assert2.default)(
    SAFE_REQUEST_ID_PATTERN.test(requestId),
    `requestId contains unsafe characters: '${requestId}'`
  );
  return (0, import_path6.join)(projectRoot, "Temp", "uLoopMCP", "compile-results", `${requestId}.json`);
}
function isUnityBusyByLockFiles(projectRoot) {
  const compilingLockPath = (0, import_path6.join)(projectRoot, "Temp", "compiling.lock");
  if ((0, import_fs5.existsSync)(compilingLockPath)) {
    return true;
  }
  const domainReloadLockPath = (0, import_path6.join)(projectRoot, "Temp", "domainreload.lock");
  return (0, import_fs5.existsSync)(domainReloadLockPath);
}
function stripUtf8Bom(content) {
  if (content.charCodeAt(0) === 65279) {
    return content.slice(1);
  }
  return content;
}
function tryReadCompileResult(projectRoot, requestId) {
  const resultFilePath = getCompileResultFilePath(projectRoot, requestId);
  if (!(0, import_fs5.existsSync)(resultFilePath)) {
    return void 0;
  }
  try {
    const content = (0, import_fs5.readFileSync)(resultFilePath, "utf-8");
    const parsed = JSON.parse(stripUtf8Bom(content));
    return parsed;
  } catch {
    return void 0;
  }
}
function canSendRequestToUnity(port) {
  return new Promise((resolve8) => {
    const socket = new net2.Socket();
    const timer = setTimeout(() => {
      socket.destroy();
      resolve8(false);
    }, READINESS_CHECK_TIMEOUT_MS);
    const cleanup = () => {
      clearTimeout(timer);
      socket.destroy();
    };
    socket.connect(port, DEFAULT_HOST2, () => {
      const rpcRequest = JSON.stringify({
        jsonrpc: "2.0",
        method: "get-tool-details",
        params: { IncludeDevelopmentOnly: false },
        id: 0
      });
      const contentLength = Buffer.byteLength(rpcRequest, "utf8");
      const frame = `${CONTENT_LENGTH_HEADER2} ${contentLength}${HEADER_SEPARATOR2}${rpcRequest}`;
      socket.write(frame);
    });
    let buffer = Buffer.alloc(0);
    socket.on("data", (chunk) => {
      buffer = Buffer.concat([buffer, chunk]);
      const sepIndex = buffer.indexOf(HEADER_SEPARATOR2);
      if (sepIndex !== -1) {
        cleanup();
        resolve8(true);
      }
    });
    socket.on("error", () => {
      cleanup();
      resolve8(false);
    });
    socket.on("close", () => {
      clearTimeout(timer);
      resolve8(false);
    });
  });
}
function sleep(ms) {
  return new Promise((resolve8) => setTimeout(resolve8, ms));
}
async function waitForCompileCompletion(options) {
  const startTime = Date.now();
  let idleSinceTimestamp = null;
  while (Date.now() - startTime < options.timeoutMs) {
    const result = tryReadCompileResult(options.projectRoot, options.requestId);
    const isBusy = isUnityBusyByLockFiles(options.projectRoot);
    if (result !== void 0 && !isBusy) {
      const now = Date.now();
      if (idleSinceTimestamp === null) {
        idleSinceTimestamp = now;
      }
      const idleDuration = now - idleSinceTimestamp;
      if (idleDuration >= LOCK_GRACE_PERIOD_MS) {
        if (options.unityPort !== void 0) {
          const isReady = await canSendRequestToUnity(options.unityPort);
          if (isReady) {
            return { outcome: "completed", result };
          }
        } else if (options.isUnityReadyWhenIdle) {
          const isReady = await options.isUnityReadyWhenIdle();
          if (isReady) {
            return { outcome: "completed", result };
          }
        } else {
          return { outcome: "completed", result };
        }
      }
    } else {
      idleSinceTimestamp = null;
    }
    await sleep(options.pollIntervalMs);
  }
  const lastResult = tryReadCompileResult(options.projectRoot, options.requestId);
  if (lastResult !== void 0 && !isUnityBusyByLockFiles(options.projectRoot)) {
    await sleep(LOCK_GRACE_PERIOD_MS);
    if (isUnityBusyByLockFiles(options.projectRoot)) {
      return { outcome: "timed_out" };
    }
    if (options.unityPort !== void 0) {
      const isReady = await canSendRequestToUnity(options.unityPort);
      if (isReady) {
        return { outcome: "completed", result: lastResult };
      }
    } else if (options.isUnityReadyWhenIdle) {
      const isReady = await options.isUnityReadyWhenIdle();
      if (isReady) {
        return { outcome: "completed", result: lastResult };
      }
    } else {
      return { outcome: "completed", result: lastResult };
    }
  }
  return { outcome: "timed_out" };
}

// src/execute-tool.ts
function suppressStdinEcho() {
  if (!process.stdin.isTTY) {
    return () => {
    };
  }
  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    terminal: false
  });
  process.stdin.setRawMode(true);
  process.stdin.resume();
  const onData = (data) => {
    if (data[0] === 3) {
      process.exit(130);
    }
  };
  process.stdin.on("data", onData);
  return () => {
    process.stdin.off("data", onData);
    process.stdin.setRawMode(false);
    process.stdin.pause();
    rl.close();
  };
}
function parseExplicitPort(portText) {
  if (portText === void 0) {
    return void 0;
  }
  const parsed = parseInt(portText, 10);
  if (isNaN(parsed)) {
    throw new Error(`Invalid port number: ${portText}`);
  }
  return parsed;
}
function stripInternalFields(result, options = {}) {
  const cleaned = { ...result };
  delete cleaned["ProjectRoot"];
  if (options.exposeServerVersion !== true) {
    delete cleaned["Ver"];
  }
  return cleaned;
}
var RETRY_DELAY_MS = 500;
var MAX_RETRIES = 3;
var SERVER_STARTING_LOCK_STALE_THRESHOLD_MS = 3e4;
var COMPILE_WAIT_TIMEOUT_MS = 9e4;
var COMPILE_WAIT_POLL_INTERVAL_MS = 100;
var FORCE_COMPILE_INDETERMINATE_MESSAGE_PREFIX = "Force compilation executed.";
var POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE = 'UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }';
var POST_COMPILE_DYNAMIC_CODE_PREWARM_USER_LIKE_CODE = 'using UnityEngine; LogType previous = Debug.unityLogger.filterLogType; Debug.unityLogger.filterLogType = LogType.Warning; try { Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { Debug.unityLogger.filterLogType = previous; }';
var POST_COMPILE_DYNAMIC_CODE_PREWARM_CODES = [
  POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE,
  POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE,
  POST_COMPILE_DYNAMIC_CODE_PREWARM_STABLE_CODE,
  POST_COMPILE_DYNAMIC_CODE_PREWARM_USER_LIKE_CODE
];
var POST_LAUNCH_DYNAMIC_CODE_PREWARM_CODES = [POST_COMPILE_DYNAMIC_CODE_PREWARM_USER_LIKE_CODE];
var POST_COMPILE_DYNAMIC_CODE_PREWARM_DELAY_MS = 500;
var POST_COMPILE_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS = 10;
var POST_LAUNCH_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS = 3;
var POST_COMPILE_DYNAMIC_CODE_PREWARM_TIMEOUT_MS = 5e3;
var POST_COMPILE_DYNAMIC_CODE_PREWARM_COMPILATION_PROVIDER_SUBSTRINGS = ["warming up"];
var POST_COMPILE_DYNAMIC_CODE_PREWARM_UNITY_ERROR_SUBSTRINGS = [
  "can only be called from the main thread"
];
var POST_COMPILE_DYNAMIC_CODE_PREWARM_TRANSIENT_ERROR_SUBSTRINGS = [
  "internal error",
  "preusingresolver.resolve",
  "system.nullreferenceexception"
];
var SKIP_SERVER_STARTING_BUSY_CHECK_ENV_KEY = "ULOOP_INTERNAL_SKIP_SERVER_STARTING_BUSY_CHECK";
var EXECUTION_IN_PROGRESS_ERROR_MESSAGE = "Another execution is already in progress";
var EXECUTION_CANCELLED_ERROR_MESSAGE = "Execution was cancelled or timed out";
var POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE = "Request timed out";
var defaultPostCompileDynamicCodePrewarmDependencies = {
  spawnCliProcess: (args) => (0, import_child_process.spawnSync)(process.execPath, [process.argv[1], ...args], {
    stdio: ["ignore", "pipe", "pipe"],
    encoding: "utf8",
    timeout: POST_COMPILE_DYNAMIC_CODE_PREWARM_TIMEOUT_MS,
    windowsHide: true,
    env: {
      ...process.env,
      [SKIP_SERVER_STARTING_BUSY_CHECK_ENV_KEY]: "1"
    }
  })
};
var defaultConnectionFailureDiagnosisDependencies = {
  findRunningUnityProcessForProjectFn: findRunningUnityProcessForProject,
  existsSyncFn: import_fs6.existsSync,
  statSyncFn: import_fs6.statSync
};
function getCompileExecutionOptions(toolName, params) {
  if (toolName !== "compile") {
    return {
      forceRecompile: false,
      waitForDomainReload: false
    };
  }
  return resolveCompileExecutionOptions(params);
}
function isRetryableError(error) {
  if (!(error instanceof Error)) {
    return false;
  }
  const message = error.message;
  return message.includes("ECONNREFUSED") || message.includes("EADDRNOTAVAIL") || message === "UNITY_NO_RESPONSE";
}
async function diagnoseRetryableProjectConnectionError(error, projectRoot, shouldDiagnoseProjectState, dependencies = defaultConnectionFailureDiagnosisDependencies) {
  if (!shouldDiagnoseProjectState || projectRoot === null || !isRetryableError(error)) {
    return error;
  }
  const runningProcess = await dependencies.findRunningUnityProcessForProjectFn(projectRoot).catch(() => void 0);
  if (runningProcess === void 0) {
    return error;
  }
  if (runningProcess === null) {
    return new UnityNotRunningError(projectRoot);
  }
  return new UnityServerNotRunningError(projectRoot);
}
async function shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldDiagnoseProjectState, dependencies = defaultConnectionFailureDiagnosisDependencies) {
  if (!shouldDiagnoseProjectState || projectRoot === null || !isRetryableProjectRecoveryError(error)) {
    return false;
  }
  const runningProcess = await dependencies.findRunningUnityProcessForProjectFn(projectRoot).catch(() => void 0);
  return runningProcess !== null && runningProcess !== void 0;
}
function isRetryableProjectRecoveryError(error) {
  if (isRetryableError(error)) {
    return true;
  }
  if (!(error instanceof Error)) {
    return false;
  }
  return isRetryableFastProjectValidationErrorMessage(error.message);
}
async function resolveRecoveryPortOrKeepCurrent(currentConnection, explicitPort, projectPath, resolveUnityConnectionFn = resolveUnityConnection) {
  if (explicitPort !== void 0) {
    return currentConnection;
  }
  try {
    return await resolveUnityConnectionFn(void 0, projectPath);
  } catch {
    if (currentConnection.requestMetadata === null || currentConnection.projectRoot === null) {
      return currentConnection;
    }
    return {
      ...currentConnection,
      requestMetadata: null,
      shouldValidateProject: true
    };
  }
}
function isServerStarting(projectRoot, dependencies = defaultConnectionFailureDiagnosisDependencies) {
  if (projectRoot === null) {
    return false;
  }
  const serverStartingLockPath = (0, import_path7.join)(projectRoot, "Temp", "serverstarting.lock");
  const existsSyncFn = dependencies.existsSyncFn ?? import_fs6.existsSync;
  const statSyncFn = dependencies.statSyncFn ?? import_fs6.statSync;
  if (!existsSyncFn(serverStartingLockPath)) {
    return false;
  }
  try {
    const lockStat = statSyncFn(serverStartingLockPath);
    return Date.now() - lockStat.mtimeMs <= SERVER_STARTING_LOCK_STALE_THRESHOLD_MS;
  } catch {
    return false;
  }
}
function isSettingsReadError(error) {
  return error instanceof Error && error.message.startsWith("Could not read Unity server port from settings.");
}
async function shouldReportServerStarting(projectRoot, shouldDiagnoseProjectState, dependencies = defaultConnectionFailureDiagnosisDependencies) {
  if (!shouldDiagnoseProjectState || !isServerStarting(projectRoot, dependencies) || projectRoot === null) {
    return false;
  }
  const runningProcess = await dependencies.findRunningUnityProcessForProjectFn(projectRoot).catch(() => void 0);
  return runningProcess !== null && runningProcess !== void 0;
}
async function shouldPromoteToServerStartingError(error, toolName, projectRoot, shouldDiagnoseProjectState, dependencies = defaultConnectionFailureDiagnosisDependencies) {
  if (!shouldPromoteServerStartingResolutionFailures(toolName)) {
    return false;
  }
  if (!isRetryableProjectRecoveryError(error) && !isSettingsReadError(error)) {
    return false;
  }
  return shouldReportServerStarting(projectRoot, shouldDiagnoseProjectState, dependencies);
}
async function resolveUnityConnectionWithStartupDiagnosis(toolName, explicitPort, projectPath, dependencies = defaultConnectionFailureDiagnosisDependencies, resolveUnityConnectionFn = resolveUnityConnection) {
  try {
    return await resolveUnityConnectionFn(explicitPort, projectPath);
  } catch (error) {
    if (!isRetryableProjectRecoveryError(error) && !isSettingsReadError(error)) {
      throw error;
    }
    const shouldDiagnoseProjectState = explicitPort === void 0;
    const projectRoot = shouldDiagnoseProjectState && projectPath !== void 0 ? validateProjectPath(projectPath) : shouldDiagnoseProjectState ? findUnityProjectRoot() : null;
    if (await shouldPromoteToServerStartingError(
      error,
      toolName,
      projectRoot,
      shouldDiagnoseProjectState,
      dependencies
    )) {
      throw createServerStartingError(error);
    }
    throw error;
  }
}
async function throwFinalToolError(error, toolName, projectRoot, shouldDiagnoseProjectState) {
  if (await shouldPromoteToServerStartingError(
    error,
    toolName,
    projectRoot,
    shouldDiagnoseProjectState
  )) {
    throw createServerStartingError(error);
  }
  const diagnosedError = await diagnoseRetryableProjectConnectionError(
    error,
    projectRoot,
    shouldDiagnoseProjectState
  );
  if (diagnosedError instanceof Error) {
    throw diagnosedError;
  }
  if (typeof diagnosedError === "string") {
    throw new Error(diagnosedError);
  }
  const serializedError = JSON.stringify(diagnosedError);
  throw new Error(serializedError ?? "Unknown error");
}
function createServerStartingError(cause) {
  if (cause instanceof Error) {
    return new Error("UNITY_SERVER_STARTING", { cause });
  }
  return new Error("UNITY_SERVER_STARTING");
}
function isTransportDisconnectError(error) {
  if (!(error instanceof Error)) {
    return false;
  }
  const message = error.message;
  return message === "UNITY_NO_RESPONSE" || message.startsWith("Connection lost:");
}
function tryParseRequestTotalMilliseconds(timings) {
  if (!Array.isArray(timings)) {
    return void 0;
  }
  const prefix = "[Perf] RequestTotal: ";
  const suffix = "ms";
  for (const timingEntry of timings) {
    if (typeof timingEntry !== "string") {
      continue;
    }
    if (!timingEntry.startsWith(prefix) || !timingEntry.endsWith(suffix)) {
      continue;
    }
    const numericText = timingEntry.slice(prefix.length, -suffix.length);
    const parsedMilliseconds = Number.parseFloat(numericText);
    if (Number.isNaN(parsedMilliseconds)) {
      continue;
    }
    return parsedMilliseconds;
  }
  return void 0;
}
function appendCliTimingsToDynamicCodeResult(result, cliTotalMilliseconds, cliProcessTotalMilliseconds) {
  const timings = result["Timings"];
  if (!Array.isArray(timings)) {
    return;
  }
  timings.push(`[Perf] CliTotal: ${cliTotalMilliseconds.toFixed(1)}ms`);
  timings.push(`[Perf] CliProcessTotal: ${cliProcessTotalMilliseconds.toFixed(1)}ms`);
  timings.push(
    `[Perf] CliBootstrap: ${Math.max(0, cliProcessTotalMilliseconds - cliTotalMilliseconds).toFixed(1)}ms`
  );
  const requestTotalMilliseconds = tryParseRequestTotalMilliseconds(timings);
  if (requestTotalMilliseconds === void 0) {
    return;
  }
  const cliOverheadMilliseconds = Math.max(0, cliTotalMilliseconds - requestTotalMilliseconds);
  timings.push(`[Perf] CliOverhead: ${cliOverheadMilliseconds.toFixed(1)}ms`);
}
function shouldPrewarmDynamicCodeAfterCompile(result) {
  const success = result["Success"];
  const errorCount = result["ErrorCount"];
  if (success === true && errorCount === 0) {
    return true;
  }
  const message = result["Message"];
  return success === null && errorCount === 0 && typeof message === "string" && message.startsWith(FORCE_COMPILE_INDETERMINATE_MESSAGE_PREFIX);
}
async function prewarmDynamicCodeAfterCompile(target, dependencies = defaultPostCompileDynamicCodePrewarmDependencies) {
  await prewarmDynamicCodeWithIsolatedCli(
    target,
    POST_COMPILE_DYNAMIC_CODE_PREWARM_CODES,
    POST_COMPILE_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS,
    dependencies
  );
}
async function prewarmDynamicCodeAfterLaunch(target, dependencies = defaultPostCompileDynamicCodePrewarmDependencies) {
  await prewarmDynamicCodeWithIsolatedCli(
    target,
    POST_LAUNCH_DYNAMIC_CODE_PREWARM_CODES,
    POST_LAUNCH_DYNAMIC_CODE_PREWARM_MAX_TOTAL_ATTEMPTS,
    dependencies
  );
}
async function prewarmDynamicCodeWithIsolatedCli(target, codes, maxTotalAttemptCount, dependencies) {
  let totalAttemptCount = 0;
  for (const code of codes) {
    const args = createPostCompileDynamicCodePrewarmArgs(target, code);
    let lastError;
    while (totalAttemptCount < maxTotalAttemptCount) {
      if (totalAttemptCount > 0) {
        await sleep(POST_COMPILE_DYNAMIC_CODE_PREWARM_DELAY_MS);
      }
      totalAttemptCount++;
      const prewarmResult = dependencies.spawnCliProcess(args);
      if (didPostCompileDynamicCodePrewarmSucceed(prewarmResult)) {
        lastError = void 0;
        break;
      }
      lastError = createPostCompileDynamicCodePrewarmError(prewarmResult);
      if (!isRetryablePostCompileDynamicCodePrewarmError(lastError)) {
        break;
      }
    }
    if (lastError !== void 0) {
      throw lastError;
    }
  }
}
function createPostCompileDynamicCodePrewarmArgs(target, code) {
  if (target.port !== void 0) {
    return [
      "execute-dynamic-code",
      "--code",
      code,
      "--yield-to-foreground-requests",
      "true",
      "--port",
      target.port.toString()
    ];
  }
  if (target.projectRoot !== void 0) {
    return [
      "execute-dynamic-code",
      "--code",
      code,
      "--yield-to-foreground-requests",
      "true",
      "--project-path",
      target.projectRoot
    ];
  }
  throw new Error("Post-compile dynamic code prewarm requires a project path or port.");
}
function didPostCompileDynamicCodePrewarmSucceed(result) {
  if (result.status !== 0) {
    return false;
  }
  if (typeof result.stdout !== "string" || result.stdout.trim().length === 0) {
    return false;
  }
  const parsed = tryParsePostCompileDynamicCodePrewarmStdout(result.stdout);
  if (parsed === void 0) {
    return false;
  }
  return parsed["Success"] === true;
}
function createPostCompileDynamicCodePrewarmError(result) {
  if (result.error !== void 0) {
    return result.error;
  }
  if (typeof result.stderr === "string") {
    const stderrError = tryParsePostCompileDynamicCodePrewarmStderr(result.stderr);
    if (stderrError !== void 0) {
      return stderrError;
    }
  }
  if (result.status === 0 && typeof result.stdout === "string" && result.stdout.trim().length > 0) {
    const parsed = tryParsePostCompileDynamicCodePrewarmStdout(result.stdout);
    if (parsed !== void 0) {
      const errorMessage = parsed["ErrorMessage"];
      if (typeof errorMessage === "string" && errorMessage.length > 0) {
        return new Error(errorMessage);
      }
    }
  }
  return new Error("Post-compile dynamic code prewarm failed.");
}
function stripAnsiControlSequences(text) {
  let result = "";
  let isInsideEscapeSequence = false;
  for (const character of text) {
    if (character === String.fromCharCode(27)) {
      isInsideEscapeSequence = true;
      continue;
    }
    if (isInsideEscapeSequence) {
      const code = character.charCodeAt(0);
      if (code >= 64 && code <= 126) {
        isInsideEscapeSequence = false;
      }
      continue;
    }
    result += character;
  }
  return result;
}
function tryParsePostCompileDynamicCodePrewarmStderr(stderr) {
  const normalizedStderr = stripAnsiControlSequences(stderr).replace(/\r/g, "");
  if (normalizedStderr.includes(EXECUTION_IN_PROGRESS_ERROR_MESSAGE)) {
    return new Error(EXECUTION_IN_PROGRESS_ERROR_MESSAGE);
  }
  if (normalizedStderr.includes(EXECUTION_CANCELLED_ERROR_MESSAGE)) {
    return new Error(EXECUTION_CANCELLED_ERROR_MESSAGE);
  }
  if (normalizedStderr.includes("Unity server is starting")) {
    return new Error("UNITY_SERVER_STARTING");
  }
  if (normalizedStderr.includes("UNITY_DOMAIN_RELOAD") || normalizedStderr.includes("Unity is reloading (Domain Reload in progress)")) {
    return new Error("UNITY_DOMAIN_RELOAD");
  }
  if (normalizedStderr.includes("UNITY_COMPILING") || normalizedStderr.includes("Unity is compiling scripts")) {
    return new Error("UNITY_COMPILING");
  }
  if (normalizedStderr.includes("UNITY_NO_RESPONSE") || normalizedStderr.includes("Cannot connect to Unity")) {
    return new Error("UNITY_NO_RESPONSE");
  }
  const connectionLostPrefix = "Connection lost:";
  const connectionLostIndex = normalizedStderr.indexOf(connectionLostPrefix);
  if (connectionLostIndex >= 0) {
    const connectionLostLine = normalizedStderr.slice(connectionLostIndex).split("\n")[0].trim();
    return new Error(connectionLostLine);
  }
  if (normalizedStderr.includes(POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE)) {
    return new Error(POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE);
  }
  return void 0;
}
function tryParsePostCompileDynamicCodePrewarmStdout(stdout) {
  try {
    return JSON.parse(stdout);
  } catch {
    return void 0;
  }
}
function isRetryablePostCompileDynamicCodePrewarmError(error) {
  return error.message === EXECUTION_IN_PROGRESS_ERROR_MESSAGE || error.message === EXECUTION_CANCELLED_ERROR_MESSAGE || error.message === POST_COMPILE_DYNAMIC_CODE_PREWARM_REQUEST_TIMEOUT_MESSAGE || error.message === "UNITY_SERVER_STARTING" || error.message === "UNITY_DOMAIN_RELOAD" || error.message === "UNITY_COMPILING" || isRetryablePostCompileDynamicCodePrewarmDisconnect(error) || isRetryablePostCompileDynamicCodePrewarmSpawnError(error) || isRetryableCompilationProviderUnavailable(error.message) || isRetryableUnityStartupMainThreadError(error.message) || isRetryablePostCompileDynamicCodeTransientError(error.message);
}
function isRetryablePostCompileDynamicCodePrewarmDisconnect(error) {
  return error.message === "UNITY_NO_RESPONSE" || error.message.startsWith("Connection lost:");
}
function isRetryablePostCompileDynamicCodePrewarmSpawnError(error) {
  return error.message.includes("ETIMEDOUT") || error.message.toLowerCase().includes("timed out");
}
function isRetryableCompilationProviderUnavailable(errorMessage) {
  if (!errorMessage.startsWith("COMPILATION_PROVIDER_UNAVAILABLE:")) {
    return false;
  }
  const normalizedMessage = errorMessage.toLowerCase();
  return POST_COMPILE_DYNAMIC_CODE_PREWARM_COMPILATION_PROVIDER_SUBSTRINGS.some(
    (substring) => normalizedMessage.includes(substring)
  );
}
function isRetryableUnityStartupMainThreadError(errorMessage) {
  const normalizedMessage = errorMessage.toLowerCase();
  return POST_COMPILE_DYNAMIC_CODE_PREWARM_UNITY_ERROR_SUBSTRINGS.some(
    (substring) => normalizedMessage.includes(substring)
  );
}
function isRetryablePostCompileDynamicCodeTransientError(errorMessage) {
  const normalizedMessage = errorMessage.toLowerCase();
  return POST_COMPILE_DYNAMIC_CODE_PREWARM_TRANSIENT_ERROR_SUBSTRINGS.some(
    (substring) => normalizedMessage.includes(substring)
  );
}
function printVersionWarning(cliVersion, serverVersion) {
  console.error("\x1B[33m\u26A0\uFE0F Version mismatch detected!\x1B[0m");
  console.error(`   Project CLI version:  ${cliVersion}`);
  console.error(`   Unity package version:${serverVersion}`);
  console.error("");
  console.error("   This may cause unexpected behavior or errors.");
  console.error("");
  console.error(
    `   Reopen Unity or reload ${PRODUCT_DISPLAY_NAME} so the package can refresh the project-local CLI.`
  );
  console.error("");
}
function checkServerVersion(result) {
  const serverVersion = result["Ver"];
  if (serverVersion && serverVersion !== VERSION) {
    printVersionWarning(VERSION, serverVersion);
  }
}
function formatToolOutput(toolName, result) {
  return stripInternalFields(result, { exposeServerVersion: toolName === "get-version" });
}
function shouldTreatServerStartingAsBusy(toolName) {
  return toolName === "execute-dynamic-code";
}
function shouldPromoteServerStartingResolutionFailures(_toolName) {
  return true;
}
async function checkUnityBusyState(toolName, projectPath) {
  const projectRoot = projectPath !== void 0 ? validateProjectPath(projectPath) : findUnityProjectRoot();
  if (projectRoot === null) {
    return;
  }
  if (shouldTreatServerStartingAsBusy(toolName) && !shouldSkipServerStartingBusyCheck() && await shouldReportServerStarting(projectRoot, true)) {
    throw new Error("UNITY_SERVER_STARTING");
  }
  const compilingLock = (0, import_path7.join)(projectRoot, "Temp", "compiling.lock");
  if ((0, import_fs6.existsSync)(compilingLock)) {
    throw new Error("UNITY_COMPILING");
  }
  const domainReloadLock = (0, import_path7.join)(projectRoot, "Temp", "domainreload.lock");
  if ((0, import_fs6.existsSync)(domainReloadLock)) {
    throw new Error("UNITY_DOMAIN_RELOAD");
  }
}
function shouldSkipServerStartingBusyCheck() {
  return process.env[SKIP_SERVER_STARTING_BUSY_CHECK_ENV_KEY] === "1";
}
async function checkUnityBusyStateBeforeProjectResolution(toolName, globalOptions) {
  if (globalOptions.port !== void 0) {
    return;
  }
  await checkUnityBusyState(toolName, globalOptions.projectPath);
}
function shouldShowInteractiveFeedback(toolName) {
  return toolName !== "execute-dynamic-code";
}
function createNoopSpinner() {
  return {
    update: (_message) => {
    },
    stop: () => {
    }
  };
}
function noop() {
}
async function executeToolCommand(toolName, params, globalOptions) {
  const commandStartedAt = Date.now();
  const portNumber = parseExplicitPort(globalOptions.port);
  await checkUnityBusyStateBeforeProjectResolution(toolName, globalOptions);
  let connection = await resolveUnityConnectionWithStartupDiagnosis(
    toolName,
    portNumber,
    globalOptions.projectPath
  );
  const compileOptions = getCompileExecutionOptions(toolName, params);
  const shouldWaitForDomainReload = compileOptions.waitForDomainReload;
  const compileRequestId = shouldWaitForDomainReload ? ensureCompileRequestId(params) : void 0;
  const shouldShowFeedback = shouldShowInteractiveFeedback(toolName);
  const restoreStdin = shouldShowFeedback ? suppressStdinEcho() : noop;
  const spinner = shouldShowFeedback ? createSpinner("Connecting to Unity...") : createNoopSpinner();
  let didCleanup = false;
  const cleanup = () => {
    if (didCleanup) {
      return;
    }
    didCleanup = true;
    spinner.stop();
    restoreStdin();
  };
  try {
    let lastError;
    let immediateResult;
    let currentProjectRoot = connection.projectRoot;
    let currentShouldDiagnoseProjectState = currentProjectRoot !== null;
    let requestDispatched = false;
    for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
      await checkUnityBusyStateBeforeProjectResolution(toolName, globalOptions);
      const projectRoot = connection.projectRoot;
      const shouldValidateProject = connection.shouldValidateProject && projectRoot !== null;
      const shouldDiagnoseProjectState = projectRoot !== null;
      currentProjectRoot = projectRoot;
      currentShouldDiagnoseProjectState = shouldDiagnoseProjectState;
      const client = new DirectUnityClient(connection.port);
      try {
        await client.connect();
        if (shouldValidateProject) {
          await validateConnectedProject(client, projectRoot);
        }
        spinner.update(`Executing ${toolName}...`);
        requestDispatched = true;
        const result = await client.sendRequest(toolName, params, {
          requestMetadata: connection.requestMetadata ?? void 0
        });
        if (result === void 0 || result === null) {
          throw new Error("UNITY_NO_RESPONSE");
        }
        immediateResult = result;
        if (!shouldWaitForDomainReload) {
          cleanup();
          if (toolName === "execute-dynamic-code") {
            appendCliTimingsToDynamicCodeResult(
              result,
              Date.now() - commandStartedAt,
              getCliProcessAgeMilliseconds()
            );
          }
          checkServerVersion(result);
          console.log(JSON.stringify(formatToolOutput(toolName, result), null, 2));
          return;
        }
        break;
      } catch (error) {
        lastError = error;
        client.disconnect();
        if (requestDispatched && shouldWaitForDomainReload) {
          if (isTransportDisconnectError(error)) {
            spinner.update("Connection lost during compile. Waiting for result file...");
            break;
          }
          cleanup();
          throw error instanceof Error ? error : new Error(String(error));
        }
        if (await shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldDiagnoseProjectState)) {
          spinner.update("Unity Editor is running, waiting for CLI Loop server to recover...");
          await sleep(RETRY_DELAY_MS);
          connection = await resolveRecoveryPortOrKeepCurrent(
            connection,
            portNumber,
            globalOptions.projectPath
          );
          continue;
        }
        if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
          break;
        }
        spinner.update("Retrying connection...");
        await sleep(RETRY_DELAY_MS);
      } finally {
        client.disconnect();
      }
    }
    if (shouldWaitForDomainReload && compileRequestId) {
      if (immediateResult === void 0 && !requestDispatched) {
        cleanup();
        if (lastError !== void 0) {
          await throwFinalToolError(
            lastError,
            toolName,
            currentProjectRoot,
            currentShouldDiagnoseProjectState
          );
        }
        throw new Error(
          "Compile request never reached Unity. Check that Unity is running and retry."
        );
      }
      const projectRootFromUnity = immediateResult !== void 0 ? immediateResult["ProjectRoot"] : void 0;
      const effectiveProjectRoot = projectRootFromUnity ?? currentProjectRoot;
      if (effectiveProjectRoot === null) {
        cleanup();
        if (immediateResult !== void 0) {
          checkServerVersion(immediateResult);
          console.log(JSON.stringify(formatToolOutput(toolName, immediateResult), null, 2));
          return;
        }
        if (lastError instanceof Error) {
          throw lastError;
        }
        throw new Error(
          "Compile request failed and project root is unknown. Check connection and retry."
        );
      }
      spinner.update("Waiting for domain reload to complete...");
      const { outcome, result: storedResult } = await waitForCompileCompletion({
        projectRoot: effectiveProjectRoot,
        requestId: compileRequestId,
        timeoutMs: COMPILE_WAIT_TIMEOUT_MS,
        pollIntervalMs: COMPILE_WAIT_POLL_INTERVAL_MS,
        unityPort: connection.port
      });
      if (outcome === "timed_out") {
        lastError = new Error(
          `Compile wait timed out after ${COMPILE_WAIT_TIMEOUT_MS}ms. Run 'uloop fix' and retry.`
        );
      } else {
        const finalResult = storedResult ?? immediateResult;
        if (finalResult !== void 0) {
          if (toolName === "compile" && shouldPrewarmDynamicCodeAfterCompile(finalResult)) {
            if (isToolEnabled("execute-dynamic-code", effectiveProjectRoot)) {
              spinner.update("Finalizing dynamic code warmup...");
              await prewarmDynamicCodeAfterCompile({
                projectRoot: portNumber === void 0 ? effectiveProjectRoot : void 0,
                port: portNumber
              });
            }
          }
          cleanup();
          if (toolName === "execute-dynamic-code") {
            appendCliTimingsToDynamicCodeResult(
              finalResult,
              Date.now() - commandStartedAt,
              getCliProcessAgeMilliseconds()
            );
          }
          checkServerVersion(finalResult);
          console.log(JSON.stringify(formatToolOutput(toolName, finalResult), null, 2));
          return;
        }
      }
    }
    cleanup();
    if (lastError === void 0) {
      throw new Error("Tool execution failed without error details.");
    }
    await throwFinalToolError(
      lastError,
      toolName,
      currentProjectRoot,
      currentShouldDiagnoseProjectState
    );
  } finally {
    cleanup();
  }
}
async function listAvailableTools(globalOptions) {
  const portNumber = parseExplicitPort(globalOptions.port);
  await checkUnityBusyStateBeforeProjectResolution("get-tool-details", globalOptions);
  let connection = await resolveUnityConnectionWithStartupDiagnosis(
    "get-tool-details",
    portNumber,
    globalOptions.projectPath
  );
  const restoreStdin = suppressStdinEcho();
  const spinner = createSpinner("Connecting to Unity...");
  let didCleanup = false;
  const cleanup = () => {
    if (didCleanup) {
      return;
    }
    didCleanup = true;
    spinner.stop();
    restoreStdin();
  };
  try {
    let lastError;
    let currentProjectRoot = connection.projectRoot;
    let currentShouldDiagnoseProjectState = currentProjectRoot !== null;
    for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
      await checkUnityBusyStateBeforeProjectResolution("get-tool-details", globalOptions);
      const projectRoot = connection.projectRoot;
      const shouldValidateProject = connection.shouldValidateProject && projectRoot !== null;
      const shouldDiagnoseProjectState = projectRoot !== null;
      currentProjectRoot = projectRoot;
      currentShouldDiagnoseProjectState = shouldDiagnoseProjectState;
      const client = new DirectUnityClient(connection.port);
      try {
        await client.connect();
        if (shouldValidateProject) {
          await validateConnectedProject(client, projectRoot);
        }
        spinner.update("Fetching tool list...");
        const result = await client.sendRequest(
          "get-tool-details",
          { IncludeDevelopmentOnly: false },
          { requestMetadata: connection.requestMetadata ?? void 0 }
        );
        if (!result.Tools || !Array.isArray(result.Tools)) {
          throw new Error("Unexpected response from Unity: missing Tools array");
        }
        cleanup();
        for (const tool of result.Tools) {
          console.log(`  - ${tool.name}`);
        }
        return;
      } catch (error) {
        lastError = error;
        client.disconnect();
        if (await shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldDiagnoseProjectState)) {
          spinner.update("Unity Editor is running, waiting for CLI Loop server to recover...");
          await sleep(RETRY_DELAY_MS);
          connection = await resolveRecoveryPortOrKeepCurrent(
            connection,
            portNumber,
            globalOptions.projectPath
          );
          continue;
        }
        if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
          break;
        }
        spinner.update("Retrying connection...");
        await sleep(RETRY_DELAY_MS);
      } finally {
        client.disconnect();
      }
    }
    cleanup();
    await throwFinalToolError(
      lastError,
      "get-tool-details",
      currentProjectRoot,
      currentShouldDiagnoseProjectState
    );
  } finally {
    cleanup();
  }
}
function convertProperties(unityProps) {
  const result = {};
  for (const [key, prop] of Object.entries(unityProps)) {
    result[key] = {
      type: prop.Type?.toLowerCase() ?? "string",
      description: prop.Description,
      default: prop.DefaultValue,
      enum: prop.Enum ?? void 0
    };
  }
  return result;
}
async function syncTools(globalOptions) {
  const portNumber = parseExplicitPort(globalOptions.port);
  await checkUnityBusyStateBeforeProjectResolution("sync-tools", globalOptions);
  let connection = await resolveUnityConnectionWithStartupDiagnosis(
    "sync-tools",
    portNumber,
    globalOptions.projectPath
  );
  const restoreStdin = suppressStdinEcho();
  const spinner = createSpinner("Connecting to Unity...");
  let didCleanup = false;
  const cleanup = () => {
    if (didCleanup) {
      return;
    }
    didCleanup = true;
    spinner.stop();
    restoreStdin();
  };
  try {
    let lastError;
    let currentProjectRoot = connection.projectRoot;
    let currentShouldDiagnoseProjectState = currentProjectRoot !== null;
    for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
      await checkUnityBusyStateBeforeProjectResolution("sync-tools", globalOptions);
      const projectRoot = connection.projectRoot;
      const shouldValidateProject = connection.shouldValidateProject && projectRoot !== null;
      const shouldDiagnoseProjectState = projectRoot !== null;
      currentProjectRoot = projectRoot;
      currentShouldDiagnoseProjectState = shouldDiagnoseProjectState;
      const client = new DirectUnityClient(connection.port);
      try {
        await client.connect();
        if (shouldValidateProject) {
          await validateConnectedProject(client, projectRoot);
        }
        spinner.update("Syncing tools...");
        const result = await client.sendRequest(
          "get-tool-details",
          { IncludeDevelopmentOnly: false },
          { requestMetadata: connection.requestMetadata ?? void 0 }
        );
        cleanup();
        if (!result.Tools || !Array.isArray(result.Tools)) {
          throw new Error("Unexpected response from Unity: missing Tools array");
        }
        const cache = {
          version: VERSION,
          serverVersion: result.Ver,
          updatedAt: (/* @__PURE__ */ new Date()).toISOString(),
          tools: result.Tools.map((tool) => ({
            name: tool.name,
            description: tool.description,
            inputSchema: {
              type: "object",
              properties: convertProperties(tool.parameterSchema.Properties),
              required: tool.parameterSchema.Required
            }
          }))
        };
        saveToolsCache(cache);
        console.log(`Synced ${cache.tools.length} tools to ${getCacheFilePath()}`);
        console.log("\nTools:");
        for (const tool of cache.tools) {
          console.log(`  - ${tool.name}`);
        }
        return;
      } catch (error) {
        lastError = error;
        client.disconnect();
        if (await shouldRetryWhenUnityProcessIsRunning(error, projectRoot, shouldDiagnoseProjectState)) {
          spinner.update("Unity Editor is running, waiting for CLI Loop server to recover...");
          await sleep(RETRY_DELAY_MS);
          connection = await resolveRecoveryPortOrKeepCurrent(
            connection,
            portNumber,
            globalOptions.projectPath
          );
          continue;
        }
        if (!isRetryableError(error) || attempt >= MAX_RETRIES) {
          break;
        }
        spinner.update("Retrying connection...");
        await sleep(RETRY_DELAY_MS);
      } finally {
        client.disconnect();
      }
    }
    cleanup();
    await throwFinalToolError(
      lastError,
      "sync-tools",
      currentProjectRoot,
      currentShouldDiagnoseProjectState
    );
  } finally {
    cleanup();
  }
}

// src/arg-parser.ts
function pascalToKebabCase(pascal) {
  const kebab = pascal.replace(/([A-Z])/g, "-$1").toLowerCase();
  return kebab.startsWith("-") ? kebab.slice(1) : kebab;
}

// src/skills/skills-manager.ts
var import_node_assert3 = __toESM(require("node:assert"), 1);
var import_fs7 = require("fs");
var import_path8 = require("path");
var import_os = require("os");

// src/skills/deprecated-skills.ts
var DEPRECATED_SKILLS = [
  "uloop-capture-window",
  // renamed to uloop-screenshot in v0.54.0
  "uloop-get-provider-details",
  // renamed to uloop-get-unity-search-providers
  "uloop-unity-search",
  // removed: replaceable by execute-dynamic-code
  "uloop-get-menu-items",
  // removed: replaceable by execute-dynamic-code
  "uloop-get-unity-search-providers",
  // removed: replaceable by execute-dynamic-code
  "uloop-execute-menu-item"
  // removed: replaceable by execute-dynamic-code
];

// src/skills/skills-manager.ts
var EXCLUDED_DIRS2 = /* @__PURE__ */ new Set([
  "node_modules",
  ".git",
  "Temp",
  "obj",
  "Build",
  "Builds",
  "Logs",
  "Skill"
]);
var EXCLUDED_FILES = /* @__PURE__ */ new Set([".meta", ".DS_Store", ".gitkeep"]);
var SkillsPathConstants = class _SkillsPathConstants {
  static PACKAGES_DIR = "Packages";
  static SRC_DIR = "src";
  static SKILLS_DIR = "skills";
  static EDITOR_DIR = "Editor";
  static API_DIR = "Api";
  static MCP_TOOLS_DIR = "McpTools";
  static SKILL_DIR = "Skill";
  static LIBRARY_DIR = "Library";
  static PACKAGE_CACHE_DIR = "PackageCache";
  static ASSETS_DIR = "Assets";
  static MANIFEST_FILE = "manifest.json";
  static SKILL_FILE = "SKILL.md";
  static MANAGED_SKILLS_DIR = "unity-cli-loop";
  static CLI_ONLY_DIR = "skill-definitions";
  static CLI_ONLY_SUBDIR = "cli-only";
  static DIST_PARENT_DIR = "..";
  static FILE_PROTOCOL = "file:";
  static PATH_PROTOCOL = "path:";
  static PACKAGE_NAME = "io.github.hatayama.uloopmcp";
  static PACKAGE_NAME_ALIAS = "io.github.hatayama.uLoopMCP";
  static PACKAGE_NAMES = [
    _SkillsPathConstants.PACKAGE_NAME,
    _SkillsPathConstants.PACKAGE_NAME_ALIAS
  ];
};
function getGlobalSkillsRoot(target) {
  return (0, import_path8.join)((0, import_os.homedir)(), target.projectDir, "skills");
}
function getProjectSkillsRoot(target) {
  const status = getUnityProjectStatus();
  if (!status.found) {
    throw new Error(
      "Not inside a Unity project. Run this command from within a Unity project directory."
    );
  }
  if (!status.hasUloop) {
    throw new Error(
      `${PRODUCT_DISPLAY_NAME} is not installed in this Unity project (${status.path}).
Please install ${PRODUCT_DISPLAY_NAME} package first, then run this command again.`
    );
  }
  return (0, import_path8.join)(status.path, target.projectDir, "skills");
}
function getSkillsBaseDir(target, global) {
  return global ? getGlobalSkillsRoot(target) : getProjectSkillsRoot(target);
}
function getManagedSkillsDir(baseDir) {
  return (0, import_path8.join)(baseDir, SkillsPathConstants.MANAGED_SKILLS_DIR);
}
function isSafeSkillPathComponent(skillDirName) {
  if (skillDirName.length === 0) {
    return false;
  }
  if (skillDirName === "." || skillDirName === "..") {
    return false;
  }
  if ((0, import_path8.isAbsolute)(skillDirName)) {
    return false;
  }
  return !skillDirName.includes("/") && !skillDirName.includes("\\") && !skillDirName.includes(import_path8.sep);
}
function assertSafeSkillPathComponent(skillDirName) {
  (0, import_node_assert3.default)(
    isSafeSkillPathComponent(skillDirName),
    "skillDirName must be a single safe path component"
  );
}
function getLegacySkillDir(baseDir, skillDirName) {
  assertSafeSkillPathComponent(skillDirName);
  return (0, import_path8.join)(baseDir, skillDirName);
}
function getManagedSkillDir(baseDir, skillDirName) {
  assertSafeSkillPathComponent(skillDirName);
  return (0, import_path8.join)(getManagedSkillsDir(baseDir), skillDirName);
}
function getLegacySkillPath(skillDirName, target, global) {
  return (0, import_path8.join)(getSkillsBaseDir(target, global), skillDirName, target.skillFileName);
}
function getManagedSkillPath(skillDirName, target, global) {
  return (0, import_path8.join)(
    getManagedSkillDir(getSkillsBaseDir(target, global), skillDirName),
    target.skillFileName
  );
}
function getPreferredSkillPath(skillDirName, target, global, groupManagedSkills) {
  return groupManagedSkills ? getManagedSkillPath(skillDirName, target, global) : getLegacySkillPath(skillDirName, target, global);
}
function getFallbackSkillPath(skillDirName, target, global, groupManagedSkills) {
  return groupManagedSkills ? getLegacySkillPath(skillDirName, target, global) : getManagedSkillPath(skillDirName, target, global);
}
function getInstalledSkillPath(skillDirName, target, global, groupManagedSkills = true, includeFallback = false) {
  const candidatePaths = [getPreferredSkillPath(skillDirName, target, global, groupManagedSkills)];
  if (includeFallback) {
    candidatePaths.push(getFallbackSkillPath(skillDirName, target, global, groupManagedSkills));
  }
  for (const candidatePath of candidatePaths) {
    if ((0, import_fs7.existsSync)(candidatePath)) {
      return candidatePath;
    }
  }
  return null;
}
function isSkillInstalled(skill, target, global, groupManagedSkills) {
  return getInstalledSkillPath(skill.dirName, target, global, groupManagedSkills) !== null;
}
function isSkillOutdated(skill, target, global, groupManagedSkills) {
  const skillPath = getInstalledSkillPath(skill.dirName, target, global, groupManagedSkills);
  if (skillPath === null) {
    return false;
  }
  const skillDir = (0, import_path8.dirname)(skillPath);
  const installedContent = (0, import_fs7.readFileSync)(skillPath, "utf-8");
  if (installedContent !== skill.content) {
    return true;
  }
  if ("additionalFiles" in skill && skill.additionalFiles) {
    const additionalFiles = skill.additionalFiles;
    for (const [relativePath, expectedContent] of Object.entries(additionalFiles)) {
      const filePath = (0, import_path8.join)(skillDir, relativePath);
      if (!(0, import_fs7.existsSync)(filePath)) {
        return true;
      }
      const installedFileContent = (0, import_fs7.readFileSync)(filePath);
      if (!installedFileContent.equals(expectedContent)) {
        return true;
      }
    }
  }
  const installedFiles = collectSkillFolderFiles(skillDir);
  const expectedFileCount = 1 + ("additionalFiles" in skill && skill.additionalFiles ? Object.keys(skill.additionalFiles).length : 0);
  const installedFileCount = 1 + (installedFiles ? Object.keys(installedFiles).length : 0);
  if (installedFileCount !== expectedFileCount) {
    return true;
  }
  return false;
}
function getSkillStatus(skill, target, global, groupManagedSkills) {
  if (!isSkillInstalled(skill, target, global, groupManagedSkills)) {
    return "not_installed";
  }
  if (isSkillOutdated(skill, target, global, groupManagedSkills)) {
    return "outdated";
  }
  return "installed";
}
function migrateLegacyManagedSkills(baseDir, managedSkillDirNames) {
  const managedRoot = getManagedSkillsDir(baseDir);
  let moved = 0;
  for (const skillDirName of new Set(managedSkillDirNames)) {
    const legacySkillDir = getLegacySkillDir(baseDir, skillDirName);
    if (!(0, import_fs7.existsSync)(legacySkillDir)) {
      continue;
    }
    const managedSkillDir = getManagedSkillDir(baseDir, skillDirName);
    if ((0, import_fs7.existsSync)(managedSkillDir)) {
      continue;
    }
    (0, import_fs7.mkdirSync)(managedRoot, { recursive: true });
    (0, import_fs7.renameSync)(legacySkillDir, managedSkillDir);
    moved++;
  }
  return moved;
}
function removeDeprecatedSkillDirs(baseDir) {
  let removed = 0;
  for (const deprecatedName of DEPRECATED_SKILLS) {
    const candidateDirs = [
      getLegacySkillDir(baseDir, deprecatedName),
      getManagedSkillDir(baseDir, deprecatedName)
    ];
    for (const candidateDir of candidateDirs) {
      if (!(0, import_fs7.existsSync)(candidateDir)) {
        continue;
      }
      (0, import_fs7.rmSync)(candidateDir, { recursive: true, force: true });
      removed++;
    }
  }
  return removed;
}
function parseFrontmatter(content) {
  const frontmatterMatch = content.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!frontmatterMatch) {
    return {};
  }
  const frontmatterMap = /* @__PURE__ */ new Map();
  const lines = frontmatterMatch[1].split(/\r?\n/);
  for (const line of lines) {
    const colonIndex = line.indexOf(":");
    if (colonIndex === -1) {
      continue;
    }
    const key = line.slice(0, colonIndex).trim();
    const rawValue = line.slice(colonIndex + 1).trim();
    let parsedValue = rawValue;
    if (rawValue === "true") {
      parsedValue = true;
    } else if (rawValue === "false") {
      parsedValue = false;
    }
    frontmatterMap.set(key, parsedValue);
  }
  return Object.fromEntries(frontmatterMap);
}
function resolveSkillSourcePath(toolPath) {
  const nestedSkillDirectory = (0, import_path8.join)(toolPath, SkillsPathConstants.SKILL_DIR);
  const nestedSkillMdPath = (0, import_path8.join)(nestedSkillDirectory, SkillsPathConstants.SKILL_FILE);
  if ((0, import_fs7.existsSync)(nestedSkillMdPath)) {
    return {
      skillDirectory: nestedSkillDirectory,
      skillMdPath: nestedSkillMdPath,
      includeSiblingFiles: true
    };
  }
  const directSkillMdPath = (0, import_path8.join)(toolPath, SkillsPathConstants.SKILL_FILE);
  if ((0, import_fs7.existsSync)(directSkillMdPath)) {
    return {
      skillDirectory: toolPath,
      skillMdPath: directSkillMdPath,
      includeSiblingFiles: false
    };
  }
  return null;
}
function scanEditorFolderForSkills(editorPath, skills, sourceType) {
  if (!(0, import_fs7.existsSync)(editorPath)) {
    return;
  }
  const entries = (0, import_fs7.readdirSync)(editorPath, { withFileTypes: true });
  for (const entry of entries) {
    if (EXCLUDED_DIRS2.has(entry.name)) {
      continue;
    }
    const fullPath = (0, import_path8.join)(editorPath, entry.name);
    if (entry.isDirectory()) {
      const skillSource = resolveSkillSourcePath(fullPath);
      if (skillSource !== null) {
        const skillDir = skillSource.skillDirectory;
        const skillMdPath = skillSource.skillMdPath;
        const content = (0, import_fs7.readFileSync)(skillMdPath, "utf-8");
        const frontmatter = parseFrontmatter(content);
        if (frontmatter.internal === true) {
          continue;
        }
        const name = typeof frontmatter.name === "string" ? frontmatter.name : entry.name;
        if (!isSafeSkillPathComponent(name)) {
          continue;
        }
        const toolName = typeof frontmatter.toolName === "string" ? frontmatter.toolName : void 0;
        const additionalFiles = skillSource.includeSiblingFiles ? collectSkillFolderFiles(skillDir) : void 0;
        skills.push({
          name,
          toolName,
          dirName: name,
          content,
          sourcePath: skillMdPath,
          additionalFiles,
          sourceType
        });
      }
      scanEditorFolderForSkills(fullPath, skills, sourceType);
    }
  }
}
function findEditorFolders(basePath, maxDepth = 2) {
  const editorFolders = [];
  function scan(currentPath, depth) {
    if (depth > maxDepth || !(0, import_fs7.existsSync)(currentPath)) {
      return;
    }
    const entries = (0, import_fs7.readdirSync)(currentPath, { withFileTypes: true });
    for (const entry of entries) {
      if (!entry.isDirectory() || EXCLUDED_DIRS2.has(entry.name)) {
        continue;
      }
      const fullPath = (0, import_path8.join)(currentPath, entry.name);
      if (entry.name === "Editor") {
        editorFolders.push(fullPath);
      } else {
        scan(fullPath, depth + 1);
      }
    }
  }
  scan(basePath, 0);
  return editorFolders;
}
function collectProjectSkills(excludedRoots = []) {
  const projectRoot = findUnityProjectRoot();
  if (!projectRoot) {
    return [];
  }
  const skills = [];
  const seenNames = /* @__PURE__ */ new Set();
  const searchPaths = getProjectSkillSearchRoots(projectRoot);
  for (const searchPath of searchPaths) {
    if (!(0, import_fs7.existsSync)(searchPath)) {
      continue;
    }
    const editorFolders = findEditorFolders(searchPath, 3);
    for (const editorFolder of editorFolders) {
      scanEditorFolderForSkills(editorFolder, skills, "project");
    }
  }
  const uniqueSkills = [];
  for (const skill of skills) {
    if (isUnderExcludedRoots(skill.sourcePath, excludedRoots)) {
      continue;
    }
    if (!seenNames.has(skill.name)) {
      seenNames.add(skill.name);
      uniqueSkills.push(skill);
    }
  }
  return uniqueSkills;
}
function getProjectSkillSearchRoots(projectRoot) {
  const searchRoots = [];
  const seenRoots = /* @__PURE__ */ new Set();
  const addSearchRoot = (root) => {
    if (!root) {
      return;
    }
    const normalizedRoot = (0, import_path8.resolve)(root);
    if (seenRoots.has(normalizedRoot)) {
      return;
    }
    seenRoots.add(normalizedRoot);
    searchRoots.push(normalizedRoot);
  };
  addSearchRoot((0, import_path8.join)(projectRoot, SkillsPathConstants.ASSETS_DIR));
  addSearchRoot(resolvePackageRoot(projectRoot));
  for (const packageRoot of enumerateDirectProjectPackageRoots(projectRoot)) {
    addSearchRoot(packageRoot);
  }
  for (const packageRoot of resolveManifestLocalPackageRoots(projectRoot)) {
    addSearchRoot(packageRoot);
  }
  for (const packageRoot of resolveDependencyPackageCacheRoots(projectRoot)) {
    addSearchRoot(packageRoot);
  }
  return searchRoots;
}
function enumerateDirectProjectPackageRoots(projectRoot) {
  const packagesRoot = (0, import_path8.join)(projectRoot, SkillsPathConstants.PACKAGES_DIR);
  if (!(0, import_fs7.existsSync)(packagesRoot)) {
    return [];
  }
  const packageRoots = [];
  const entries = (0, import_fs7.readdirSync)(packagesRoot, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }
    packageRoots.push(resolveSkillSearchRootCandidate((0, import_path8.join)(packagesRoot, entry.name)));
  }
  return packageRoots;
}
function resolveManifestLocalPackageRoots(projectRoot) {
  const dependencies = readManifestDependencies(projectRoot);
  if (!dependencies) {
    return [];
  }
  const packageRoots = [];
  for (const dependencyValue of Object.values(dependencies)) {
    const localPath = resolveLocalDependencyPath(dependencyValue, projectRoot);
    if (!localPath) {
      continue;
    }
    packageRoots.push(resolveSkillSearchRootCandidate(localPath));
  }
  return packageRoots;
}
function resolveDependencyPackageCacheRoots(projectRoot) {
  const dependencies = readManifestDependencies(projectRoot);
  if (!dependencies) {
    return [];
  }
  const dependencyNames = new Set(Object.keys(dependencies).map((name) => name.toLowerCase()));
  if (dependencyNames.size === 0) {
    return [];
  }
  const packageCacheDir = (0, import_path8.join)(
    projectRoot,
    SkillsPathConstants.LIBRARY_DIR,
    SkillsPathConstants.PACKAGE_CACHE_DIR
  );
  if (!(0, import_fs7.existsSync)(packageCacheDir)) {
    return [];
  }
  const packageRoots = [];
  const entries = (0, import_fs7.readdirSync)(packageCacheDir, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }
    const separatorIndex = entry.name.indexOf("@");
    const dependencyName = separatorIndex === -1 ? entry.name : entry.name.slice(0, separatorIndex);
    if (!dependencyNames.has(dependencyName.toLowerCase())) {
      continue;
    }
    packageRoots.push(resolveSkillSearchRootCandidate((0, import_path8.join)(packageCacheDir, entry.name)));
  }
  return packageRoots;
}
function resolveSkillSearchRootCandidate(candidate) {
  const nestedRoot = (0, import_path8.join)(candidate, SkillsPathConstants.PACKAGES_DIR, SkillsPathConstants.SRC_DIR);
  if ((0, import_fs7.existsSync)(nestedRoot)) {
    return nestedRoot;
  }
  return candidate;
}
function getAllSkillStatuses(target, global, groupManagedSkills = true) {
  const allSkills = collectAllSkills();
  return allSkills.map((skill) => ({
    name: skill.name,
    status: getSkillStatus(skill, target, global, groupManagedSkills),
    path: getInstalledSkillPath(skill.dirName, target, global, groupManagedSkills, true) ?? void 0,
    source: skill.sourceType === "project" ? "project" : "bundled"
  }));
}
function getPreferredSkillDir(baseDir, skillDirName, groupManagedSkills) {
  assertSafeSkillPathComponent(skillDirName);
  return groupManagedSkills ? getManagedSkillDir(baseDir, skillDirName) : getLegacySkillDir(baseDir, skillDirName);
}
function installSkill(skill, target, global, groupManagedSkills) {
  const baseDir = getSkillsBaseDir(target, global);
  const skillDir = getPreferredSkillDir(baseDir, skill.dirName, groupManagedSkills);
  syncInstalledSkillDirectory(skillDir, target.skillFileName, skill.content, skill.additionalFiles);
  const alternateSkillDir = getPreferredSkillDir(baseDir, skill.dirName, !groupManagedSkills);
  if (alternateSkillDir !== skillDir && (0, import_fs7.existsSync)(alternateSkillDir)) {
    (0, import_fs7.rmSync)(alternateSkillDir, { recursive: true, force: true });
  }
}
function syncInstalledSkillDirectory(skillDir, skillFileName, skillContent, additionalFiles) {
  (0, import_fs7.mkdirSync)((0, import_path8.dirname)(skillDir), { recursive: true });
  const tempSkillDir = (0, import_fs7.mkdtempSync)(`${skillDir}.tmp-`);
  const skillPath = (0, import_path8.join)(tempSkillDir, skillFileName);
  let replaced = false;
  try {
    (0, import_fs7.writeFileSync)(skillPath, skillContent, "utf-8");
    if (additionalFiles) {
      for (const [relativePath, content] of Object.entries(additionalFiles)) {
        const fullPath = (0, import_path8.join)(tempSkillDir, relativePath);
        (0, import_fs7.mkdirSync)((0, import_path8.dirname)(fullPath), { recursive: true });
        (0, import_fs7.writeFileSync)(fullPath, content);
      }
    }
    (0, import_fs7.rmSync)(skillDir, { recursive: true, force: true });
    (0, import_fs7.renameSync)(tempSkillDir, skillDir);
    replaced = true;
  } finally {
    if (!replaced) {
      (0, import_fs7.rmSync)(tempSkillDir, { recursive: true, force: true });
    }
  }
}
function uninstallSkill(skill, target, global, groupManagedSkills) {
  const baseDir = getSkillsBaseDir(target, global);
  const candidateDirs = [getPreferredSkillDir(baseDir, skill.dirName, groupManagedSkills)];
  let removed = false;
  for (const candidateDir of candidateDirs) {
    if (!(0, import_fs7.existsSync)(candidateDir)) {
      continue;
    }
    (0, import_fs7.rmSync)(candidateDir, { recursive: true, force: true });
    removed = true;
  }
  return removed;
}
function uninstallSkillFromAllLayouts(skill, target, global) {
  const baseDir = getSkillsBaseDir(target, global);
  const candidateDirs = [
    getManagedSkillDir(baseDir, skill.dirName),
    getLegacySkillDir(baseDir, skill.dirName)
  ];
  let removed = false;
  for (const candidateDir of candidateDirs) {
    if (!(0, import_fs7.existsSync)(candidateDir)) {
      continue;
    }
    (0, import_fs7.rmSync)(candidateDir, { recursive: true, force: true });
    removed = true;
  }
  return removed;
}
function installAllSkills(target, global, groupManagedSkills = true) {
  const result = {
    installed: 0,
    updated: 0,
    skipped: 0,
    bundledCount: 0,
    projectCount: 0,
    deprecatedRemoved: 0
  };
  const allSkills = collectAllSkills();
  const baseDir = getSkillsBaseDir(target, global);
  result.deprecatedRemoved = removeDeprecatedSkillDirs(baseDir);
  if (groupManagedSkills) {
    migrateLegacyManagedSkills(
      baseDir,
      allSkills.map((skill) => skill.dirName)
    );
  }
  const disabledTools = global ? [] : loadDisabledTools();
  const projectSkills = allSkills.filter((skill) => skill.sourceType === "project");
  const nonProjectSkills = allSkills.filter((skill) => skill.sourceType !== "project");
  for (const skill of allSkills) {
    if (isSkillDisabledByToolSettings(skill, disabledTools)) {
      uninstallSkillFromAllLayouts(skill, target, global);
      continue;
    }
    const status = getSkillStatus(skill, target, global, groupManagedSkills);
    if (status === "not_installed") {
      installSkill(skill, target, global, groupManagedSkills);
      result.installed++;
    } else if (status === "outdated") {
      installSkill(skill, target, global, groupManagedSkills);
      result.updated++;
    } else {
      result.skipped++;
    }
  }
  result.bundledCount = nonProjectSkills.length;
  result.projectCount = projectSkills.length;
  return result;
}
function isSkillDisabledByToolSettings(skill, disabledTools) {
  if (disabledTools.length === 0) {
    return false;
  }
  const toolName = skill.toolName ?? (skill.name.startsWith("uloop-") ? skill.name.slice("uloop-".length) : null);
  if (toolName === null) {
    return false;
  }
  return disabledTools.includes(toolName);
}
function uninstallAllSkills(target, global, groupManagedSkills = true) {
  const result = { removed: 0, notFound: 0 };
  const baseDir = getSkillsBaseDir(target, global);
  result.removed += removeDeprecatedSkillDirs(baseDir);
  const allSkills = collectAllSkills();
  for (const skill of allSkills) {
    if (uninstallSkill(skill, target, global, groupManagedSkills)) {
      result.removed++;
    } else {
      result.notFound++;
    }
  }
  return result;
}
function getInstallDir(target, global, groupManagedSkills = true) {
  const baseDir = getSkillsBaseDir(target, global);
  return groupManagedSkills ? getManagedSkillsDir(baseDir) : baseDir;
}
function getTotalSkillCount() {
  return collectAllSkills().length;
}
function collectAllSkills() {
  const projectRoot = findUnityProjectRoot();
  const packageRoot = projectRoot ? resolvePackageRoot(projectRoot) : null;
  const packageSkills = packageRoot ? collectPackageSkillsFromRoot(packageRoot) : [];
  const cliOnlySkills = collectCliOnlySkills();
  const projectSkills = collectProjectSkills(packageRoot ? [packageRoot] : []);
  return dedupeSkillsByName([packageSkills, cliOnlySkills, projectSkills]);
}
function collectPackageSkillsFromRoot(packageRoot) {
  const mcpToolsRoot = (0, import_path8.join)(
    packageRoot,
    SkillsPathConstants.EDITOR_DIR,
    SkillsPathConstants.API_DIR,
    SkillsPathConstants.MCP_TOOLS_DIR
  );
  if (!(0, import_fs7.existsSync)(mcpToolsRoot)) {
    return [];
  }
  const skills = [];
  scanEditorFolderForSkills(mcpToolsRoot, skills, "package");
  return skills;
}
function collectCliOnlySkills() {
  const cliOnlyRoot = (0, import_path8.resolve)(
    __dirname,
    SkillsPathConstants.DIST_PARENT_DIR,
    SkillsPathConstants.SRC_DIR,
    SkillsPathConstants.SKILLS_DIR,
    SkillsPathConstants.CLI_ONLY_DIR,
    SkillsPathConstants.CLI_ONLY_SUBDIR
  );
  if (!(0, import_fs7.existsSync)(cliOnlyRoot)) {
    return [];
  }
  const skills = [];
  scanEditorFolderForSkills(cliOnlyRoot, skills, "cli-only");
  return skills;
}
function isExcludedFile(fileName) {
  if (EXCLUDED_FILES.has(fileName)) {
    return true;
  }
  for (const pattern of EXCLUDED_FILES) {
    if (fileName.endsWith(pattern)) {
      return true;
    }
  }
  return false;
}
function collectSkillFolderFilesRecursive(baseDir, currentDir, additionalFiles) {
  const entries = (0, import_fs7.readdirSync)(currentDir, { withFileTypes: true });
  for (const entry of entries) {
    if (isExcludedFile(entry.name)) {
      continue;
    }
    const fullPath = (0, import_path8.join)(currentDir, entry.name);
    const relativePath = fullPath.slice(baseDir.length + 1);
    if (entry.isDirectory()) {
      if (EXCLUDED_DIRS2.has(entry.name)) {
        continue;
      }
      collectSkillFolderFilesRecursive(baseDir, fullPath, additionalFiles);
    } else if (entry.isFile()) {
      if (entry.name === SkillsPathConstants.SKILL_FILE) {
        continue;
      }
      additionalFiles[relativePath] = (0, import_fs7.readFileSync)(fullPath);
    }
  }
}
function collectSkillFolderFiles(skillDir) {
  if (!(0, import_fs7.existsSync)(skillDir)) {
    return void 0;
  }
  const additionalFiles = {};
  collectSkillFolderFilesRecursive(skillDir, skillDir, additionalFiles);
  return Object.keys(additionalFiles).length > 0 ? additionalFiles : void 0;
}
function dedupeSkillsByName(skillGroups) {
  const seenNames = /* @__PURE__ */ new Set();
  const merged = [];
  for (const group of skillGroups) {
    for (const skill of group) {
      if (seenNames.has(skill.name)) {
        continue;
      }
      seenNames.add(skill.name);
      merged.push(skill);
    }
  }
  return merged;
}
function resolvePackageRoot(projectRoot) {
  const candidates = [];
  candidates.push((0, import_path8.join)(projectRoot, SkillsPathConstants.PACKAGES_DIR, SkillsPathConstants.SRC_DIR));
  const manifestPaths = resolveManifestPackagePaths(projectRoot);
  for (const manifestPath of manifestPaths) {
    candidates.push(manifestPath);
  }
  for (const packageName of SkillsPathConstants.PACKAGE_NAMES) {
    candidates.push((0, import_path8.join)(projectRoot, SkillsPathConstants.PACKAGES_DIR, packageName));
  }
  const directRoot = resolveFirstPackageRoot(candidates);
  if (directRoot) {
    return directRoot;
  }
  return resolvePackageCacheRoot(projectRoot);
}
function resolveManifestPackagePaths(projectRoot) {
  const dependencies = readManifestDependencies(projectRoot);
  if (!dependencies) {
    return [];
  }
  const resolvedPaths = [];
  for (const [dependencyName, dependencyValue] of Object.entries(dependencies)) {
    if (!isTargetPackageName(dependencyName)) {
      continue;
    }
    const localPath = resolveLocalDependencyPath(dependencyValue, projectRoot);
    if (localPath) {
      resolvedPaths.push(localPath);
    }
  }
  return resolvedPaths;
}
function readManifestDependencies(projectRoot) {
  const manifestPath = (0, import_path8.join)(
    projectRoot,
    SkillsPathConstants.PACKAGES_DIR,
    SkillsPathConstants.MANIFEST_FILE
  );
  if (!(0, import_fs7.existsSync)(manifestPath)) {
    return null;
  }
  const manifestContent = (0, import_fs7.readFileSync)(manifestPath, "utf-8");
  let manifestJson;
  try {
    manifestJson = JSON.parse(manifestContent);
  } catch (error) {
    console.warn("Failed to parse manifest.json; skipping manifest-based path resolution.", error);
    return null;
  }
  const dependencies = manifestJson.dependencies;
  if (!dependencies) {
    return null;
  }
  return dependencies;
}
function resolveLocalDependencyPath(dependencyValue, projectRoot) {
  if (dependencyValue.startsWith(SkillsPathConstants.FILE_PROTOCOL)) {
    const rawPath = dependencyValue.slice(SkillsPathConstants.FILE_PROTOCOL.length);
    return resolveDependencyPath(rawPath, projectRoot);
  }
  if (dependencyValue.startsWith(SkillsPathConstants.PATH_PROTOCOL)) {
    const rawPath = dependencyValue.slice(SkillsPathConstants.PATH_PROTOCOL.length);
    return resolveDependencyPath(rawPath, projectRoot);
  }
  return null;
}
function resolveDependencyPath(rawPath, projectRoot) {
  const trimmed = rawPath.trim();
  if (!trimmed) {
    return null;
  }
  let normalizedPath = trimmed;
  if (normalizedPath.startsWith("//")) {
    normalizedPath = normalizedPath.slice(2);
  }
  if ((0, import_path8.isAbsolute)(normalizedPath)) {
    return normalizedPath;
  }
  return (0, import_path8.resolve)(projectRoot, normalizedPath);
}
function resolveFirstPackageRoot(candidates) {
  for (const candidate of candidates) {
    const resolvedRoot = resolvePackageRootCandidate(candidate);
    if (resolvedRoot) {
      return resolvedRoot;
    }
  }
  return null;
}
function resolvePackageCacheRoot(projectRoot) {
  const packageCacheDir = (0, import_path8.join)(
    projectRoot,
    SkillsPathConstants.LIBRARY_DIR,
    SkillsPathConstants.PACKAGE_CACHE_DIR
  );
  if (!(0, import_fs7.existsSync)(packageCacheDir)) {
    return null;
  }
  const entries = (0, import_fs7.readdirSync)(packageCacheDir, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }
    if (!isTargetPackageCacheDir(entry.name)) {
      continue;
    }
    const candidate = (0, import_path8.join)(packageCacheDir, entry.name);
    const resolvedRoot = resolvePackageRootCandidate(candidate);
    if (resolvedRoot) {
      return resolvedRoot;
    }
  }
  return null;
}
function resolvePackageRootCandidate(candidate) {
  if (!(0, import_fs7.existsSync)(candidate)) {
    return null;
  }
  const directToolsPath = (0, import_path8.join)(
    candidate,
    SkillsPathConstants.EDITOR_DIR,
    SkillsPathConstants.API_DIR,
    SkillsPathConstants.MCP_TOOLS_DIR
  );
  if ((0, import_fs7.existsSync)(directToolsPath)) {
    return candidate;
  }
  const nestedRoot = (0, import_path8.join)(candidate, SkillsPathConstants.PACKAGES_DIR, SkillsPathConstants.SRC_DIR);
  const nestedToolsPath = (0, import_path8.join)(
    nestedRoot,
    SkillsPathConstants.EDITOR_DIR,
    SkillsPathConstants.API_DIR,
    SkillsPathConstants.MCP_TOOLS_DIR
  );
  if ((0, import_fs7.existsSync)(nestedToolsPath)) {
    return nestedRoot;
  }
  return null;
}
function isTargetPackageName(name) {
  const normalized = name.toLowerCase();
  return SkillsPathConstants.PACKAGE_NAMES.some(
    (packageName) => packageName.toLowerCase() === normalized
  );
}
function isTargetPackageCacheDir(dirName) {
  const normalized = dirName.toLowerCase();
  return SkillsPathConstants.PACKAGE_NAMES.some(
    (packageName) => normalized.startsWith(`${packageName.toLowerCase()}@`)
  );
}
function isUnderExcludedRoots(targetPath, excludedRoots) {
  for (const root of excludedRoots) {
    if (isPathUnder(targetPath, root)) {
      return true;
    }
  }
  return false;
}
function isPathUnder(childPath, parentPath) {
  const resolvedChild = (0, import_path8.resolve)(childPath);
  const resolvedParent = (0, import_path8.resolve)(parentPath);
  if (resolvedChild === resolvedParent) {
    return true;
  }
  return resolvedChild.startsWith(resolvedParent + import_path8.sep);
}

// src/skills/target-config.ts
var TARGET_CONFIGS = {
  claude: {
    id: "claude",
    displayName: "Claude Code",
    projectDir: ".claude",
    skillFileName: "SKILL.md"
  },
  codex: {
    id: "codex",
    displayName: "Codex CLI",
    projectDir: ".codex",
    skillFileName: "SKILL.md"
  },
  cursor: {
    id: "cursor",
    displayName: "Cursor",
    projectDir: ".cursor",
    skillFileName: "SKILL.md"
  },
  gemini: {
    id: "gemini",
    displayName: "Gemini CLI",
    projectDir: ".gemini",
    skillFileName: "SKILL.md"
  },
  agents: {
    id: "agents",
    displayName: "Other (.agents)",
    projectDir: ".agents",
    skillFileName: "SKILL.md"
  },
  windsurf: {
    id: "windsurf",
    displayName: "Windsurf",
    projectDir: ".agents",
    skillFileName: "SKILL.md"
  },
  antigravity: {
    id: "antigravity",
    displayName: "Antigravity",
    projectDir: ".agent",
    skillFileName: "SKILL.md"
  }
};
var ALL_TARGET_IDS = [
  "claude",
  "codex",
  "cursor",
  "gemini",
  "agents",
  "antigravity"
];
function getTargetConfig(id) {
  return TARGET_CONFIGS[id];
}

// src/skills/skills-command.ts
function registerSkillsCommand(program2) {
  const skillsCmd = program2.command("skills").description("Manage uloop skills for AI coding tools");
  skillsCmd.command("list").description("List all uloop skills and their installation status").option("-g, --global", "Check global installation").option("--flat", "Install directly under skills/ instead of skills/unity-cli-loop/").option("--claude", "Check Claude Code installation").option("--codex", "Check Codex CLI installation").option("--cursor", "Check Cursor installation").option("--gemini", "Check Gemini CLI installation").option("--agents", "Check generic .agents installation").option("--windsurf", "Check Windsurf installation").option("--antigravity", "Check Antigravity installation").action((options) => {
    const targets = resolveTargets(options);
    const global = options.global ?? false;
    listSkills(targets, global, !(options.flat ?? false));
  });
  skillsCmd.command("install").description("Install all uloop skills").option("-g, --global", "Install to global location").option("--flat", "Install directly under skills/ instead of skills/unity-cli-loop/").option("--claude", "Install to Claude Code").option("--codex", "Install to Codex CLI").option("--cursor", "Install to Cursor").option("--gemini", "Install to Gemini CLI").option("--agents", "Install to generic .agents target").option("--windsurf", "Install to Windsurf").option("--antigravity", "Install to Antigravity").action((options) => {
    const targets = resolveTargets(options);
    if (targets.length === 0) {
      showTargetGuidance("install");
      return;
    }
    installSkills(targets, options.global ?? false, !(options.flat ?? false));
  });
  skillsCmd.command("uninstall").description("Uninstall all uloop skills").option("-g, --global", "Uninstall from global location").option("--flat", "Uninstall skills installed directly under skills/").option("--claude", "Uninstall from Claude Code").option("--codex", "Uninstall from Codex CLI").option("--cursor", "Uninstall from Cursor").option("--gemini", "Uninstall from Gemini CLI").option("--agents", "Uninstall from generic .agents target").option("--windsurf", "Uninstall from Windsurf").option("--antigravity", "Uninstall from Antigravity").action((options) => {
    const targets = resolveTargets(options);
    if (targets.length === 0) {
      showTargetGuidance("uninstall");
      return;
    }
    uninstallSkills(targets, options.global ?? false, !(options.flat ?? false));
  });
}
function resolveTargets(options) {
  const targets = [];
  if (options.claude) {
    targets.push(getTargetConfig("claude"));
  }
  if (options.codex) {
    targets.push(getTargetConfig("codex"));
  }
  if (options.cursor) {
    targets.push(getTargetConfig("cursor"));
  }
  if (options.gemini) {
    targets.push(getTargetConfig("gemini"));
  }
  if (options.agents) {
    targets.push(getTargetConfig("agents"));
  }
  if (options.windsurf) {
    targets.push(getTargetConfig("windsurf"));
  }
  if (options.antigravity) {
    targets.push(getTargetConfig("antigravity"));
  }
  return targets;
}
function showTargetGuidance(command) {
  console.log(`
Please specify at least one target for '${command}':`);
  console.log("");
  console.log("Available targets:");
  console.log("  --claude        Claude Code (.claude/skills/unity-cli-loop/)");
  console.log("  --codex         Codex CLI (.codex/skills/unity-cli-loop/)");
  console.log("  --cursor        Cursor (.cursor/skills/unity-cli-loop/)");
  console.log("  --gemini        Gemini CLI (.gemini/skills/unity-cli-loop/)");
  console.log("  --agents        Other (.agents) (.agents/skills/unity-cli-loop/)");
  console.log("  --windsurf      Windsurf (.agents/skills/unity-cli-loop/)");
  console.log("  --antigravity   Antigravity (.agent/skills/unity-cli-loop/)");
  console.log("");
  console.log("Options:");
  console.log("  -g, --global   Use global location");
  console.log("  --flat         Use skills/ directly instead of skills/unity-cli-loop/");
  console.log("");
  console.log("Examples:");
  console.log(`  uloop skills ${command} --claude`);
  console.log(`  uloop skills ${command} --cursor --global`);
  console.log(`  uloop skills ${command} --claude --codex --cursor --gemini --agents`);
}
function listSkills(targets, global, groupManagedSkills) {
  const location = global ? "Global" : "Project";
  const targetConfigs = targets.length > 0 ? targets : ALL_TARGET_IDS.map(getTargetConfig);
  console.log(`
uloop Skills Status:`);
  console.log("");
  for (const target of targetConfigs) {
    const dir = getInstallDir(target, global, groupManagedSkills);
    console.log(`${target.displayName} (${location}):`);
    console.log(`Location: ${dir}`);
    console.log("=".repeat(50));
    const statuses = getAllSkillStatuses(target, global, groupManagedSkills);
    for (const skill of statuses) {
      const icon = getStatusIcon(skill.status);
      const statusText = getStatusText(skill.status);
      console.log(`  ${icon} ${skill.name} (${statusText})`);
    }
    console.log("");
  }
  console.log(`Total: ${getTotalSkillCount()} skills`);
}
function getStatusIcon(status) {
  switch (status) {
    case "installed":
      return "\x1B[32m\u2713\x1B[0m";
    case "outdated":
      return "\x1B[33m\u2191\x1B[0m";
    case "not_installed":
      return "\x1B[31m\u2717\x1B[0m";
    default:
      return "?";
  }
}
function getStatusText(status) {
  switch (status) {
    case "installed":
      return "installed";
    case "outdated":
      return "outdated";
    case "not_installed":
      return "not installed";
    default:
      return "unknown";
  }
}
function installSkills(targets, global, groupManagedSkills) {
  const location = global ? "global" : "project";
  console.log(`
Installing uloop skills (${location})...`);
  console.log("");
  for (const target of targets) {
    const dir = getInstallDir(target, global, groupManagedSkills);
    const result = installAllSkills(target, global, groupManagedSkills);
    console.log(`${target.displayName}:`);
    console.log(`  \x1B[32m\u2713\x1B[0m Installed: ${result.installed}`);
    console.log(`  \x1B[33m\u2191\x1B[0m Updated: ${result.updated}`);
    console.log(`  \x1B[90m-\x1B[0m Skipped (up-to-date): ${result.skipped}`);
    if (result.deprecatedRemoved > 0) {
      console.log(`  \x1B[31m\u2717\x1B[0m Deprecated removed: ${result.deprecatedRemoved}`);
    }
    console.log(`  Location: ${dir}`);
    console.log("");
  }
}
function uninstallSkills(targets, global, groupManagedSkills) {
  const location = global ? "global" : "project";
  console.log(`
Uninstalling uloop skills (${location})...`);
  console.log("");
  for (const target of targets) {
    const dir = getInstallDir(target, global, groupManagedSkills);
    const result = uninstallAllSkills(target, global, groupManagedSkills);
    console.log(`${target.displayName}:`);
    console.log(`  \x1B[31m\u2717\x1B[0m Removed: ${result.removed}`);
    console.log(`  \x1B[90m-\x1B[0m Not found: ${result.notFound}`);
    console.log(`  Location: ${dir}`);
    console.log("");
  }
}

// src/commands/launch.ts
var import_path10 = require("path");

// node_modules/launch-unity/dist/lib.js
var import_node_child_process2 = require("node:child_process");
var import_node_fs2 = require("node:fs");
var import_promises4 = require("node:fs/promises");
var import_node_path2 = require("node:path");
var import_node_util2 = require("node:util");

// node_modules/launch-unity/dist/launchUnityProcess.js
function launchUnityProcess(spawnProcess, unityPath, args, onSpawned) {
  return new Promise((resolve8, reject) => {
    const child = spawnProcess(unityPath, args, {
      stdio: "ignore",
      detached: true,
      // Git Bash (MSYS) rewrites Windows-style paths unless the launch opts out.
      env: {
        ...process.env,
        MSYS_NO_PATHCONV: "1"
      }
    });
    const handleError = (error) => {
      child.removeListener("spawn", handleSpawn);
      reject(new Error(`Failed to launch Unity: ${error.message}`));
    };
    const handleSpawn = () => {
      child.removeListener("error", handleError);
      try {
        onSpawned();
      } catch (error) {
        reject(error);
        return;
      }
      child.unref();
      resolve8();
    };
    child.once("error", handleError);
    child.once("spawn", handleSpawn);
  });
}

// node_modules/launch-unity/dist/unityHub.js
var import_promises3 = require("node:fs/promises");
var import_node_fs = require("node:fs");
var import_node_path = require("node:path");
var import_node_assert4 = __toESM(require("node:assert"), 1);
var resolveUnityHubProjectFiles = () => {
  if (process.platform === "darwin") {
    const home = process.env.HOME;
    if (!home) {
      return [];
    }
    const base = (0, import_node_path.join)(home, "Library", "Application Support", "UnityHub");
    return [(0, import_node_path.join)(base, "projects-v1.json"), (0, import_node_path.join)(base, "projects.json")];
  }
  if (process.platform === "win32") {
    const appData = process.env.APPDATA;
    if (!appData) {
      return [];
    }
    const base = (0, import_node_path.join)(appData, "UnityHub");
    return [(0, import_node_path.join)(base, "projects-v1.json"), (0, import_node_path.join)(base, "projects.json")];
  }
  return [];
};
var removeTrailingSeparators = (target) => {
  let trimmed = target;
  while (trimmed.length > 1 && (trimmed.endsWith("/") || trimmed.endsWith("\\"))) {
    trimmed = trimmed.slice(0, -1);
  }
  return trimmed;
};
var normalizePath2 = (target) => {
  const resolvedPath = (0, import_node_path.resolve)(target);
  return removeTrailingSeparators(resolvedPath);
};
var resolvePathWithActualCase = (target) => {
  try {
    return removeTrailingSeparators(import_node_fs.realpathSync.native(target));
  } catch {
    return normalizePath2(target);
  }
};
var toComparablePath = (value) => {
  return value.replace(/\\/g, "/").toLocaleLowerCase();
};
var pathsEqual = (left, right) => {
  return toComparablePath(normalizePath2(left)) === toComparablePath(normalizePath2(right));
};
var safeParseProjectsJson = (content) => {
  try {
    return JSON.parse(content);
  } catch {
    return void 0;
  }
};
var logDebug = (message) => {
  if (process.env["LAUNCH_UNITY_DEBUG"] === "1") {
    console.log(`[unityHub] ${message}`);
  }
};
var ensureProjectEntryAndUpdate = async (projectPath, version, when, setFavorite = false) => {
  const canonicalProjectPath = resolvePathWithActualCase(projectPath);
  const projectTitle = (0, import_node_path.basename)(canonicalProjectPath);
  const containingFolderPath = (0, import_node_path.dirname)(canonicalProjectPath);
  const candidates = resolveUnityHubProjectFiles();
  if (candidates.length === 0) {
    logDebug("No Unity Hub project files found.");
    return;
  }
  for (const path of candidates) {
    logDebug(`Trying Unity Hub file: ${path}`);
    const content = await (0, import_promises3.readFile)(path, "utf8").catch(() => void 0);
    if (!content) {
      logDebug("Read failed or empty content, skipping.");
      continue;
    }
    const json = safeParseProjectsJson(content);
    if (!json) {
      logDebug("Parse failed, skipping.");
      continue;
    }
    const data = { ...json.data ?? {} };
    const existingKey = Object.keys(data).find((key) => {
      const entryPath = data[key]?.path;
      return entryPath ? pathsEqual(entryPath, projectPath) : false;
    });
    const targetKey = existingKey ?? canonicalProjectPath;
    const existingEntry = existingKey ? data[existingKey] : void 0;
    logDebug(existingKey ? `Found existing entry for project (key=${existingKey}). Updating lastModified.` : `No existing entry. Adding new entry (key=${targetKey}).`);
    const updatedEntry = {
      ...existingEntry,
      path: existingEntry?.path ?? canonicalProjectPath,
      containingFolderPath: existingEntry?.containingFolderPath ?? containingFolderPath,
      version: existingEntry?.version ?? version,
      title: existingEntry?.title ?? projectTitle,
      lastModified: when.getTime(),
      isFavorite: setFavorite ? true : existingEntry?.isFavorite ?? false
    };
    const updatedJson = {
      ...json,
      data: {
        ...data,
        [targetKey]: updatedEntry
      }
    };
    try {
      await (0, import_promises3.writeFile)(path, JSON.stringify(updatedJson, void 0, 2), "utf8");
      logDebug("Write succeeded.");
    } catch (error) {
      logDebug(`Write failed: ${error instanceof Error ? error.message : String(error)}`);
    }
    return;
  }
};
var updateLastModifiedIfExists = async (projectPath, when) => {
  const candidates = resolveUnityHubProjectFiles();
  if (candidates.length === 0) {
    return;
  }
  for (const path of candidates) {
    let content;
    let json;
    try {
      content = await (0, import_promises3.readFile)(path, "utf8");
    } catch {
      continue;
    }
    try {
      json = JSON.parse(content);
    } catch {
      continue;
    }
    if (!json.data) {
      return;
    }
    const projectKey = Object.keys(json.data).find((key) => {
      const entryPath = json.data?.[key]?.path;
      return entryPath ? pathsEqual(entryPath, projectPath) : false;
    });
    if (!projectKey) {
      return;
    }
    const original = json.data[projectKey];
    if (!original) {
      return;
    }
    json.data[projectKey] = {
      ...original,
      lastModified: when.getTime()
    };
    try {
      await (0, import_promises3.writeFile)(path, JSON.stringify(json, void 0, 2), "utf8");
    } catch {
    }
    return;
  }
};
var resolveUnityHubProjectsInfoFile = () => {
  if (process.platform === "darwin") {
    const home = process.env.HOME;
    if (!home) {
      return void 0;
    }
    return (0, import_node_path.join)(home, "Library", "Application Support", "UnityHub", "projectsInfo.json");
  }
  if (process.platform === "win32") {
    const appData = process.env.APPDATA;
    if (!appData) {
      return void 0;
    }
    return (0, import_node_path.join)(appData, "UnityHub", "projectsInfo.json");
  }
  return void 0;
};
var parseCliArgs = (cliArgsString) => {
  (0, import_node_assert4.default)(cliArgsString !== null && cliArgsString !== void 0, "cliArgsString must not be null");
  const trimmed = cliArgsString.trim();
  if (trimmed.length === 0) {
    return [];
  }
  const tokens = [];
  let current = "";
  let inQuote = null;
  for (const char of trimmed) {
    if (inQuote !== null) {
      if (char === inQuote) {
        tokens.push(current);
        current = "";
        inQuote = null;
      } else {
        current += char;
      }
      continue;
    }
    if (char === '"' || char === "'") {
      inQuote = char;
      continue;
    }
    if (char === " ") {
      if (current.length > 0) {
        tokens.push(current);
        current = "";
      }
      continue;
    }
    current += char;
  }
  if (current.length > 0) {
    tokens.push(current);
  }
  return tokens;
};
var groupCliArgs = (args) => {
  const groups = [];
  let current = "";
  for (const arg of args) {
    if (arg.startsWith("-") && current.length > 0) {
      groups.push(current);
      current = arg;
    } else if (current.length === 0) {
      current = arg;
    } else {
      current += ` ${arg}`;
    }
  }
  if (current.length > 0) {
    groups.push(current);
  }
  return groups;
};
var getProjectCliArgs = async (projectPath) => {
  (0, import_node_assert4.default)(projectPath !== null && projectPath !== void 0, "projectPath must not be null");
  const infoFilePath = resolveUnityHubProjectsInfoFile();
  if (!infoFilePath) {
    logDebug("projectsInfo.json path could not be resolved.");
    return [];
  }
  logDebug(`Reading projectsInfo.json: ${infoFilePath}`);
  let content;
  try {
    content = await (0, import_promises3.readFile)(infoFilePath, "utf8");
  } catch {
    logDebug("projectsInfo.json not found or not readable.");
    return [];
  }
  let json;
  try {
    json = JSON.parse(content);
  } catch {
    logDebug("projectsInfo.json parse failed.");
    return [];
  }
  const normalizedProjectPath = normalizePath2(projectPath);
  const projectKey = Object.keys(json).find((key) => pathsEqual(key, normalizedProjectPath));
  if (!projectKey) {
    logDebug(`No entry found for project: ${normalizedProjectPath}`);
    return [];
  }
  const projectInfo = json[projectKey];
  const cliArgsString = projectInfo?.cliArgs;
  if (!cliArgsString || cliArgsString.trim().length === 0) {
    logDebug("cliArgs is empty or not defined.");
    return [];
  }
  const parsed = parseCliArgs(cliArgsString);
  logDebug(`Parsed Unity Hub cliArgs: ${JSON.stringify(parsed)}`);
  return parsed;
};

// node_modules/launch-unity/dist/lib.js
var execFileAsync2 = (0, import_node_util2.promisify)(import_node_child_process2.execFile);
var UNITY_EXECUTABLE_PATTERN_MAC = /Unity\.app\/Contents\/MacOS\/Unity/i;
var UNITY_EXECUTABLE_PATTERN_WINDOWS = /Unity\.exe/i;
var PROJECT_PATH_PATTERN = /-(?:projectPath|projectpath)(?:=|\s+)("[^"]+"|'[^']+'|.+?)(?=\s+-[a-zA-Z]|$)/i;
var PROCESS_LIST_COMMAND_MAC = "ps";
var PROCESS_LIST_ARGS_MAC = ["-axo", "pid=,command=", "-ww"];
var WINDOWS_POWERSHELL = "powershell";
var UNITY_LOCKFILE_NAME = "UnityLockfile";
var TEMP_DIRECTORY_NAME = "Temp";
var ASSETS_DIRECTORY_NAME = "Assets";
var RECOVERY_DIRECTORY_NAME = "_Recovery";
var UNITY_STARTUP_WAIT_MESSAGE = "Waiting for Unity to finish starting...";
function getUnityVersion(projectPath) {
  const versionFile = (0, import_node_path2.join)(projectPath, "ProjectSettings", "ProjectVersion.txt");
  if (!(0, import_node_fs2.existsSync)(versionFile)) {
    throw new Error(`ProjectVersion.txt not found at ${versionFile}. This does not appear to be a Unity project.`);
  }
  const content = (0, import_node_fs2.readFileSync)(versionFile, "utf8");
  const version = content.match(/m_EditorVersion:\s*([^\s\n]+)/)?.[1];
  if (!version) {
    throw new Error(`Could not extract Unity version from ${versionFile}`);
  }
  return version;
}
function getUnityPathWindows(version) {
  const candidates = [];
  const programFiles = process.env["PROGRAMFILES"];
  const programFilesX86 = process.env["PROGRAMFILES(X86)"];
  const localAppData = process.env["LOCALAPPDATA"];
  const addCandidate = (base) => {
    if (!base) {
      return;
    }
    candidates.push((0, import_node_path2.join)(base, "Unity", "Hub", "Editor", version, "Editor", "Unity.exe"));
  };
  addCandidate(programFiles);
  addCandidate(programFilesX86);
  addCandidate(localAppData);
  candidates.push((0, import_node_path2.join)("C:\\", "Program Files", "Unity", "Hub", "Editor", version, "Editor", "Unity.exe"));
  for (const candidate of candidates) {
    if ((0, import_node_fs2.existsSync)(candidate)) {
      return candidate;
    }
  }
  return candidates[0] ?? (0, import_node_path2.join)("C:\\", "Program Files", "Unity", "Hub", "Editor", version, "Editor", "Unity.exe");
}
function getUnityPath(version) {
  if (process.platform === "darwin") {
    return `/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity`;
  }
  if (process.platform === "win32") {
    return getUnityPathWindows(version);
  }
  return `/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity`;
}
var removeTrailingSeparators2 = (target) => {
  let trimmed = target;
  while (trimmed.length > 1 && (trimmed.endsWith("/") || trimmed.endsWith("\\"))) {
    trimmed = trimmed.slice(0, -1);
  }
  return trimmed;
};
var normalizePath3 = (target) => {
  const resolvedPath = (0, import_node_path2.resolve)(target);
  const trimmed = removeTrailingSeparators2(resolvedPath);
  return trimmed;
};
var toComparablePath2 = (value) => {
  return value.replace(/\\/g, "/").toLocaleLowerCase();
};
var pathsEqual2 = (left, right) => {
  return toComparablePath2(normalizePath3(left)) === toComparablePath2(normalizePath3(right));
};
function extractProjectPath(command) {
  const match = command.match(PROJECT_PATH_PATTERN);
  if (!match) {
    return void 0;
  }
  const raw = match[1];
  if (!raw) {
    return void 0;
  }
  const trimmed = raw.trim();
  if (trimmed.startsWith('"') && trimmed.endsWith('"')) {
    return trimmed.slice(1, -1);
  }
  if (trimmed.startsWith("'") && trimmed.endsWith("'")) {
    return trimmed.slice(1, -1);
  }
  return trimmed;
}
var isUnityAuxiliaryProcess = (command) => {
  const normalizedCommand = command.toLowerCase();
  if (normalizedCommand.includes("-batchmode")) {
    return true;
  }
  return normalizedCommand.includes("assetimportworker");
};
async function listUnityProcessesMac() {
  let stdout = "";
  try {
    const result = await execFileAsync2(PROCESS_LIST_COMMAND_MAC, PROCESS_LIST_ARGS_MAC);
    stdout = result.stdout;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`Failed to retrieve Unity process list: ${message}`);
    return [];
  }
  const lines = stdout.split("\n").map((line) => line.trim()).filter((line) => line.length > 0);
  const processes = [];
  for (const line of lines) {
    const match = line.match(/^(\d+)\s+(.*)$/);
    if (!match) {
      continue;
    }
    const pidValue = Number.parseInt(match[1] ?? "", 10);
    if (!Number.isFinite(pidValue)) {
      continue;
    }
    const command = match[2] ?? "";
    if (!UNITY_EXECUTABLE_PATTERN_MAC.test(command)) {
      continue;
    }
    if (isUnityAuxiliaryProcess(command)) {
      continue;
    }
    const projectArgument = extractProjectPath(command);
    if (!projectArgument) {
      continue;
    }
    processes.push({
      pid: pidValue,
      projectPath: normalizePath3(projectArgument)
    });
  }
  return processes;
}
async function listUnityProcessesWindows() {
  const scriptLines = [
    "$ErrorActionPreference = 'Stop'",
    `$processes = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine }`,
    "foreach ($process in $processes) {",
    `  $commandLine = $process.CommandLine -replace "\`r", ' ' -replace "\`n", ' '`,
    '  Write-Output ("{0}|{1}" -f $process.ProcessId, $commandLine)',
    "}"
  ];
  let stdout = "";
  try {
    const result = await execFileAsync2(WINDOWS_POWERSHELL, ["-NoProfile", "-Command", scriptLines.join("\n")]);
    stdout = result.stdout ?? "";
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`Failed to retrieve Unity process list on Windows: ${message}`);
    return [];
  }
  const lines = stdout.split("\n").map((line) => line.trim()).filter((line) => line.length > 0);
  const processes = [];
  for (const line of lines) {
    const delimiterIndex = line.indexOf("|");
    if (delimiterIndex < 0) {
      continue;
    }
    const pidText = line.slice(0, delimiterIndex).trim();
    const command = line.slice(delimiterIndex + 1).trim();
    const pidValue = Number.parseInt(pidText, 10);
    if (!Number.isFinite(pidValue)) {
      continue;
    }
    if (!UNITY_EXECUTABLE_PATTERN_WINDOWS.test(command)) {
      continue;
    }
    if (isUnityAuxiliaryProcess(command)) {
      continue;
    }
    const projectArgument = extractProjectPath(command);
    if (!projectArgument) {
      continue;
    }
    processes.push({
      pid: pidValue,
      projectPath: normalizePath3(projectArgument)
    });
  }
  return processes;
}
async function listUnityProcesses() {
  if (process.platform === "darwin") {
    return await listUnityProcessesMac();
  }
  if (process.platform === "win32") {
    return await listUnityProcessesWindows();
  }
  return [];
}
async function findRunningUnityProcess(projectPath) {
  const normalizedTarget = normalizePath3(projectPath);
  const processes = await listUnityProcesses();
  return processes.find((candidate) => pathsEqual2(candidate.projectPath, normalizedTarget));
}
async function focusUnityProcess(pid) {
  if (process.platform === "darwin") {
    await focusUnityProcessMac(pid);
    return;
  }
  if (process.platform === "win32") {
    await focusUnityProcessWindows(pid);
  }
}
function isMacAutomationPermissionError(error) {
  const message = error instanceof Error ? error.message : String(error);
  return message.includes("(-1743)") || message.includes("Not authorized to send Apple events");
}
async function isGuiProcessMac(pid) {
  const script = `tell application "System Events" to exists (first process whose unix id is ${pid})`;
  try {
    const result = await execFileAsync2("osascript", ["-e", script]);
    return result.stdout.trim() === "true" ? "visible" : "not-visible";
  } catch (error) {
    if (isMacAutomationPermissionError(error)) {
      return "unknown";
    }
    return "not-visible";
  }
}
async function isGuiProcessWindows(pid) {
  const scriptLines = [
    "$ErrorActionPreference = 'Stop'",
    `$proc = Get-Process -Id ${pid} -ErrorAction Stop`,
    "if ($proc.MainWindowHandle -ne 0) { Write-Output 'true' } else { Write-Output 'false' }"
  ];
  try {
    const result = await execFileAsync2(WINDOWS_POWERSHELL, ["-NoProfile", "-Command", scriptLines.join("\n")]);
    return result.stdout.trim() === "true" ? "visible" : "not-visible";
  } catch {
    return "unknown";
  }
}
async function isGuiProcess(pid) {
  if (process.platform === "darwin") {
    return await isGuiProcessMac(pid);
  }
  if (process.platform === "win32") {
    return await isGuiProcessWindows(pid);
  }
  return "unknown";
}
async function focusUnityProcessMac(pid) {
  const script = `tell application "System Events" to set frontmost of (first process whose unix id is ${pid}) to true`;
  try {
    await execFileAsync2("osascript", ["-e", script]);
    console.log("Brought existing Unity to the front.");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`Failed to bring Unity to front: ${message}`);
  }
}
async function focusUnityProcessWindows(pid) {
  const addTypeLines = [
    'Add-Type -TypeDefinition @"',
    "using System;",
    "using System.Runtime.InteropServices;",
    "public static class Win32Interop {",
    '  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);',
    '  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);',
    "}",
    '"@'
  ];
  const scriptLines = [
    "$ErrorActionPreference = 'Stop'",
    ...addTypeLines,
    `try { $process = Get-Process -Id ${pid} -ErrorAction Stop } catch { return }`,
    "$handle = $process.MainWindowHandle",
    "if ($handle -eq 0) { return }",
    "[Win32Interop]::ShowWindowAsync($handle, 9) | Out-Null",
    "[Win32Interop]::SetForegroundWindow($handle) | Out-Null"
  ];
  try {
    await execFileAsync2(WINDOWS_POWERSHELL, ["-NoProfile", "-Command", scriptLines.join("\n")]);
    console.log("Brought existing Unity to the front.");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`Failed to bring Unity to front on Windows: ${message}`);
  }
}
async function isLockfileHeldMac(lockfilePath) {
  try {
    const result = await execFileAsync2("lsof", [lockfilePath]);
    const lines = result.stdout.split("\n").filter((line) => line.length > 0);
    return lines.length > 1;
  } catch {
    return false;
  }
}
async function isLockfileHeldWindows(lockfilePath) {
  const escapedPath = lockfilePath.replace(/'/g, "''");
  const scriptLines = [
    "try {",
    `  $f = [System.IO.File]::Open('${escapedPath}', [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)`,
    "  $f.Close()",
    "  Write-Output 'UNLOCKED'",
    "} catch [System.IO.IOException] {",
    "  Write-Output 'LOCKED'",
    "} catch {",
    "  Write-Output 'UNLOCKED'",
    "}"
  ];
  try {
    const result = await execFileAsync2(WINDOWS_POWERSHELL, ["-NoProfile", "-Command", scriptLines.join("\n")]);
    return result.stdout.trim() === "LOCKED";
  } catch {
    return false;
  }
}
async function isLockfileHeld(lockfilePath) {
  if (process.platform === "darwin") {
    return await isLockfileHeldMac(lockfilePath);
  }
  if (process.platform === "win32") {
    return await isLockfileHeldWindows(lockfilePath);
  }
  return false;
}
async function handleStaleLockfile(projectPath) {
  const tempDirectoryPath = (0, import_node_path2.join)(projectPath, TEMP_DIRECTORY_NAME);
  const lockfilePath = (0, import_node_path2.join)(tempDirectoryPath, UNITY_LOCKFILE_NAME);
  if (!(0, import_node_fs2.existsSync)(lockfilePath)) {
    return;
  }
  if (await isLockfileHeld(lockfilePath)) {
    throw new Error(`Unity appears to be running for this project (lockfile is held: ${lockfilePath}). Use -r to restart or -q to quit the running instance.`);
  }
  console.log(`UnityLockfile found without active Unity process: ${lockfilePath}`);
  console.log("Assuming previous crash. Cleaning Temp directory and continuing launch.");
  try {
    await (0, import_promises4.rm)(tempDirectoryPath, { recursive: true, force: true });
    console.log("Deleted Temp directory.");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`Failed to delete Temp directory: ${message}`);
  }
  try {
    await (0, import_promises4.rm)(lockfilePath, { force: true });
    console.log("Deleted UnityLockfile.");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`Failed to delete UnityLockfile: ${message}`);
  }
  console.log();
}
async function deleteRecoveryDirectory(projectPath) {
  const recoveryPath = (0, import_node_path2.join)(projectPath, ASSETS_DIRECTORY_NAME, RECOVERY_DIRECTORY_NAME);
  if (!(0, import_node_fs2.existsSync)(recoveryPath)) {
    return;
  }
  console.log(`Deleting recovery directory: ${recoveryPath}`);
  try {
    await (0, import_promises4.rm)(recoveryPath, { recursive: true, force: true });
    console.log("Deleted recovery directory.");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`Failed to delete recovery directory: ${message}`);
  }
}
var LOCKFILE_POLL_INTERVAL_MS = 100;
var LOCKFILE_WAIT_TIMEOUT_MS = 5e3;
var KILL_POLL_INTERVAL_MS = 100;
var KILL_TIMEOUT_MS = 1e4;
var GRACEFUL_QUIT_TIMEOUT_MS = 1e4;
function isProcessAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}
function killProcess(pid) {
  try {
    process.kill(pid, "SIGKILL");
  } catch {
  }
}
async function waitForProcessExit(pid, timeoutMs) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    if (!isProcessAlive(pid)) {
      return true;
    }
    await new Promise((resolve8) => setTimeout(resolve8, KILL_POLL_INTERVAL_MS));
  }
  return false;
}
async function killRunningUnity(projectPath) {
  const processInfo = await findRunningUnityProcess(projectPath);
  if (!processInfo) {
    console.log("No running Unity process found for this project.");
    console.log();
    return;
  }
  const pid = processInfo.pid;
  console.log(`Killing Unity (PID: ${pid})...`);
  killProcess(pid);
  const exited = await waitForProcessExit(pid, KILL_TIMEOUT_MS);
  if (!exited) {
    throw new Error(`Failed to kill Unity (PID: ${pid}) within ${KILL_TIMEOUT_MS / 1e3}s.`);
  }
  console.log("Unity killed.");
  console.log();
}
async function sendGracefulQuitMac(pid) {
  const script = [
    'tell application "System Events"',
    `  set frontmost of (first process whose unix id is ${pid}) to true`,
    '  keystroke "q" using {command down}',
    "end tell"
  ].join("\n");
  try {
    await execFileAsync2("osascript", ["-e", script]);
  } catch {
  }
}
async function sendGracefulQuitWindows(pid) {
  const scriptLines = [
    "$ErrorActionPreference = 'Stop'",
    `try { $proc = Get-Process -Id ${pid} -ErrorAction Stop; $proc.CloseMainWindow() | Out-Null } catch { }`
  ];
  try {
    await execFileAsync2(WINDOWS_POWERSHELL, ["-NoProfile", "-Command", scriptLines.join("\n")]);
  } catch {
  }
}
async function sendGracefulQuit(pid) {
  if (process.platform === "darwin") {
    await sendGracefulQuitMac(pid);
    return;
  }
  if (process.platform === "win32") {
    await sendGracefulQuitWindows(pid);
    return;
  }
}
async function quitRunningUnity(projectPath) {
  const processInfo = await findRunningUnityProcess(projectPath);
  if (!processInfo) {
    console.log("No running Unity process found for this project.");
    return;
  }
  const pid = processInfo.pid;
  console.log(`Quitting Unity (PID: ${pid})...`);
  await sendGracefulQuit(pid);
  console.log(`Sent graceful quit signal. Waiting up to ${GRACEFUL_QUIT_TIMEOUT_MS / 1e3}s...`);
  const exitedGracefully = await waitForProcessExit(pid, GRACEFUL_QUIT_TIMEOUT_MS);
  if (exitedGracefully) {
    console.log("Unity quit gracefully.");
    return;
  }
  console.log("Unity did not respond to graceful quit. Force killing...");
  killProcess(pid);
  const exitedAfterKill = await waitForProcessExit(pid, KILL_TIMEOUT_MS);
  if (!exitedAfterKill) {
    throw new Error(`Failed to kill Unity (PID: ${pid}) within ${KILL_TIMEOUT_MS / 1e3}s.`);
  }
  console.log("Unity force killed.");
}
function hasBuildTargetArg(unityArgs) {
  for (const arg of unityArgs) {
    if (arg === "-buildTarget") {
      return true;
    }
    if (arg.startsWith("-buildTarget=")) {
      return true;
    }
  }
  return false;
}
var EXCLUDED_DIR_NAMES = /* @__PURE__ */ new Set([
  "library",
  "temp",
  "logs",
  "obj",
  ".git",
  "node_modules",
  ".idea",
  ".vscode",
  ".vs"
]);
function isUnityProjectRoot(candidateDir) {
  const versionFile = (0, import_node_path2.join)(candidateDir, "ProjectSettings", "ProjectVersion.txt");
  return (0, import_node_fs2.existsSync)(versionFile);
}
function listSubdirectoriesSorted(dir) {
  let entries = [];
  try {
    const dirents = (0, import_node_fs2.readdirSync)(dir, { withFileTypes: true });
    const subdirs = dirents.filter((d) => d.isDirectory()).map((d) => d.name).filter((name) => !EXCLUDED_DIR_NAMES.has(name.toLocaleLowerCase()));
    subdirs.sort((a, b) => a.localeCompare(b));
    entries = subdirs.map((name) => (0, import_node_path2.join)(dir, name));
  } catch {
    entries = [];
  }
  return entries;
}
function findUnityProjectBfs(rootDir, maxDepth) {
  const queue = [];
  let rootCanonical;
  try {
    rootCanonical = (0, import_node_fs2.realpathSync)(rootDir);
  } catch {
    rootCanonical = rootDir;
  }
  queue.push({ dir: rootCanonical, depth: 0 });
  const visited = /* @__PURE__ */ new Set([toComparablePath2(normalizePath3(rootCanonical))]);
  while (queue.length > 0) {
    const current = queue.shift();
    if (!current) {
      continue;
    }
    const { dir, depth } = current;
    if (isUnityProjectRoot(dir)) {
      return normalizePath3(dir);
    }
    const canDescend = maxDepth === -1 || depth < maxDepth;
    if (!canDescend) {
      continue;
    }
    const children = listSubdirectoriesSorted(dir);
    for (const child of children) {
      let childCanonical = child;
      try {
        const stat = (0, import_node_fs2.lstatSync)(child);
        if (stat.isSymbolicLink()) {
          try {
            childCanonical = (0, import_node_fs2.realpathSync)(child);
          } catch {
            continue;
          }
        }
      } catch {
        continue;
      }
      const key = toComparablePath2(normalizePath3(childCanonical));
      if (visited.has(key)) {
        continue;
      }
      visited.add(key);
      queue.push({ dir: childCanonical, depth: depth + 1 });
    }
  }
  return void 0;
}
async function waitForLockfile(projectPath) {
  const lockfilePath = (0, import_node_path2.join)(projectPath, TEMP_DIRECTORY_NAME, UNITY_LOCKFILE_NAME);
  const start = Date.now();
  while (Date.now() - start < LOCKFILE_WAIT_TIMEOUT_MS) {
    if ((0, import_node_fs2.existsSync)(lockfilePath)) {
      return;
    }
    await new Promise((resolve8) => setTimeout(resolve8, LOCKFILE_POLL_INTERVAL_MS));
  }
  console.warn("Unity launched, but UnityLockfile was not detected within 5s.");
}
async function launch(opts) {
  const { projectPath, platform, unityArgs, unityVersion } = opts;
  const unityPath = getUnityPath(unityVersion);
  if (!(0, import_node_fs2.existsSync)(unityPath)) {
    throw new Error(`Unity ${unityVersion} not found at ${unityPath}. Please install Unity through Unity Hub.`);
  }
  console.log("Opening Unity...");
  console.log(`Project Path: ${projectPath}`);
  console.log(`Detected Unity version: ${unityVersion}`);
  const args = ["-projectPath", projectPath];
  const unityArgsContainBuildTarget = hasBuildTargetArg(unityArgs);
  if (platform && platform.length > 0 && !unityArgsContainBuildTarget) {
    args.push("-buildTarget", platform);
  }
  const hubCliArgs = await getProjectCliArgs(projectPath);
  if (hubCliArgs.length > 0) {
    console.log("Unity Hub launch options:");
    for (const line of groupCliArgs(hubCliArgs)) {
      console.log(`  ${line}`);
    }
    args.push(...hubCliArgs);
  } else {
    console.log("Unity Hub launch options: none");
  }
  if (unityArgs.length > 0) {
    args.push(...unityArgs);
  }
  return launchUnityProcess(import_node_child_process2.spawn, unityPath, args, () => {
    console.log(UNITY_STARTUP_WAIT_MESSAGE);
  });
}
function shouldWaitForUnityStartup(action) {
  return action === "launched" || action === "killed-and-launched";
}
async function orchestrateLaunch(options) {
  if (options.quit && options.restart) {
    throw new Error("--quit and --restart cannot be used together.");
  }
  let resolvedProjectPath = options.projectPath;
  if (!resolvedProjectPath) {
    const depthInfo = options.searchMaxDepth === -1 ? "unlimited" : String(options.searchMaxDepth);
    console.log(`Searching for Unity project under ${options.searchRoot} (max-depth: ${depthInfo})...`);
    const found = findUnityProjectBfs(options.searchRoot, options.searchMaxDepth);
    if (!found) {
      throw new Error(`Unity project not found under ${options.searchRoot}.`);
    }
    console.log();
    resolvedProjectPath = found;
  }
  if (!(0, import_node_fs2.existsSync)(resolvedProjectPath)) {
    throw new Error(`Project directory not found: ${resolvedProjectPath}`);
  }
  const unityVersion = getUnityVersion(resolvedProjectPath);
  if (options.addUnityHub || options.favoriteUnityHub) {
    console.log(`Detected Unity version: ${unityVersion}`);
    console.log(`Project Path: ${resolvedProjectPath}`);
    const now2 = /* @__PURE__ */ new Date();
    await ensureProjectEntryAndUpdate(resolvedProjectPath, unityVersion, now2, options.favoriteUnityHub);
    console.log("Unity Hub entry updated.");
    return { action: "hub-updated", projectPath: resolvedProjectPath, unityVersion };
  }
  if (options.quit) {
    await quitRunningUnity(resolvedProjectPath);
    return { action: "quit", projectPath: resolvedProjectPath };
  }
  const isRestart = options.restart;
  if (isRestart) {
    await killRunningUnity(resolvedProjectPath);
  } else {
    const runningProcess = await findRunningUnityProcess(resolvedProjectPath);
    if (runningProcess) {
      const guiState = await isGuiProcess(runningProcess.pid);
      if (guiState === "not-visible") {
        throw new Error(`Unity process (PID: ${runningProcess.pid}) found but has no visible window. Use -r to restart or -q to quit the running instance.`);
      }
      if (guiState === "unknown") {
        console.warn("Could not verify Unity window visibility. Trying to focus the existing Unity process anyway.");
      }
      console.log(`Unity process already running for project: ${resolvedProjectPath} (PID: ${runningProcess.pid})`);
      await focusUnityProcess(runningProcess.pid);
      return { action: "focused", projectPath: resolvedProjectPath, pid: runningProcess.pid };
    }
  }
  if (options.deleteRecovery) {
    await deleteRecoveryDirectory(resolvedProjectPath);
  }
  await handleStaleLockfile(resolvedProjectPath);
  const resolved = {
    projectPath: resolvedProjectPath,
    platform: options.platform,
    unityArgs: options.unityArgs,
    unityVersion
  };
  await launch(resolved);
  const now = /* @__PURE__ */ new Date();
  try {
    await updateLastModifiedIfExists(resolvedProjectPath, now);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`Failed to update Unity Hub lastModified: ${message}`);
  }
  const action = isRestart ? "killed-and-launched" : "launched";
  if (shouldWaitForUnityStartup(action)) {
    await waitForLockfile(resolvedProjectPath);
  }
  return { action, projectPath: resolvedProjectPath, unityVersion };
}

// src/launch-readiness.ts
var import_fs8 = require("fs");
var import_path9 = require("path");
var LAUNCH_READINESS_TIMEOUT_MS = 18e4;
var LAUNCH_READINESS_RETRY_MS = 1e3;
var LAUNCH_READINESS_REQUIRED_STABLE_PROBE_COUNT = 3;
var LAUNCH_READINESS_STABLE_CODE = 'UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }';
var LAUNCH_READINESS_USER_LIKE_CODE = 'using UnityEngine; LogType previous = Debug.unityLogger.filterLogType; Debug.unityLogger.filterLogType = LogType.Warning; try { Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { Debug.unityLogger.filterLogType = previous; }';
var LAUNCH_READINESS_REQUEST_TOTAL_THRESHOLD_MS = 250;
var LAUNCH_READINESS_SETTLE_TIMEOUT_MS = 1e4;
var TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_MESSAGES = [
  "Another execution is already in progress",
  "Execution was cancelled or timed out",
  "Internal error"
];
var TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_SUBSTRINGS = [
  "PreUsingResolver.Resolve",
  "System.NullReferenceException"
];
var TRANSIENT_COMPILATION_PROVIDER_UNAVAILABLE_SUBSTRINGS = ["warming up"];
var RETRYABLE_UNITY_ERROR_SUBSTRINGS = ["can only be called from the main thread"];
var defaultDependencies2 = {
  resolveUnityConnectionFn: resolveUnityConnection,
  createClient: (port) => new DirectUnityClient(port),
  sleepFn: sleep,
  nowFn: () => Date.now(),
  isProjectBusyFn: isProjectBusyByLockFiles
};
function isProjectBusyByLockFiles(projectPath) {
  const compilingLockPath = (0, import_path9.join)(projectPath, "Temp", "compiling.lock");
  if ((0, import_fs8.existsSync)(compilingLockPath)) {
    return true;
  }
  const domainReloadLockPath = (0, import_path9.join)(projectPath, "Temp", "domainreload.lock");
  return (0, import_fs8.existsSync)(domainReloadLockPath);
}
function isTransientExecuteDynamicCodeFailure(payload) {
  if (payload === void 0 || payload === null) {
    return true;
  }
  if (typeof payload.Success !== "boolean") {
    return true;
  }
  if (payload.Success) {
    return false;
  }
  const errorMessage = payload.ErrorMessage ?? "";
  if (errorMessage.length === 0) {
    return true;
  }
  if (TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_MESSAGES.includes(errorMessage)) {
    return true;
  }
  if (TRANSIENT_EXECUTE_DYNAMIC_CODE_ERROR_SUBSTRINGS.some(
    (substring) => errorMessage.includes(substring)
  )) {
    return true;
  }
  return isTransientCompilationProviderUnavailable(errorMessage) || isRetryableUnityStartupError(`Unity error: ${errorMessage}`);
}
function isTransientCompilationProviderUnavailable(errorMessage) {
  if (!errorMessage.startsWith("COMPILATION_PROVIDER_UNAVAILABLE:")) {
    return false;
  }
  const normalizedMessage = errorMessage.toLowerCase();
  return TRANSIENT_COMPILATION_PROVIDER_UNAVAILABLE_SUBSTRINGS.some(
    (substring) => normalizedMessage.includes(substring)
  );
}
function isRetryableLaunchReadinessError(error) {
  if (error instanceof ProjectMismatchError) {
    return true;
  }
  if (error instanceof UnityNotRunningError) {
    return true;
  }
  if (!(error instanceof Error)) {
    return false;
  }
  const message = error.message;
  return isRetryableFastProjectValidationErrorMessage(message) || message.includes("Could not read Unity server port from settings") || message.includes("ECONNREFUSED") || message.includes("EADDRNOTAVAIL") || message === "UNITY_NO_RESPONSE" || message.startsWith("Connection lost:") || isRetryableUnityStartupError(message);
}
function isRetryableUnityStartupError(message) {
  if (!message.startsWith("Unity error:")) {
    return false;
  }
  const normalizedMessage = message.toLowerCase();
  return RETRYABLE_UNITY_ERROR_SUBSTRINGS.some(
    (substring) => normalizedMessage.includes(substring)
  );
}
function createLaunchReadinessFailure(payload) {
  const errorMessage = payload.ErrorMessage ?? "unknown execute-dynamic-code error";
  return new Error(`execute-dynamic-code launch readiness probe failed: ${errorMessage}`);
}
function parseTimingMilliseconds(timings, label) {
  if (timings === void 0) {
    return null;
  }
  const prefix = `[Perf] ${label}: `;
  const entry = timings.find((timing) => timing.startsWith(prefix));
  if (entry === void 0) {
    return null;
  }
  const valueText = entry.slice(prefix.length).replace(/ms$/, "");
  const value = Number.parseFloat(valueText);
  if (Number.isNaN(value)) {
    return null;
  }
  return value;
}
function isLaunchReadinessStable(payload) {
  const requestTotalMilliseconds = parseTimingMilliseconds(payload.Timings, "RequestTotal");
  if (requestTotalMilliseconds === null) {
    return true;
  }
  return requestTotalMilliseconds <= LAUNCH_READINESS_REQUEST_TOTAL_THRESHOLD_MS;
}
function hasFastSessionMetadata(connection) {
  return connection.requestMetadata !== null;
}
async function validateLaunchConnectionIdentity(client, connection) {
  if (connection.requestMetadata !== null) {
    const response = await client.sendRequest(
      "ping",
      { Message: "launch-readiness" },
      { requestMetadata: connection.requestMetadata }
    );
    if (typeof response?.Message !== "string") {
      throw new Error("Unexpected response from Unity: missing ping message");
    }
    return;
  }
  if (connection.projectRoot !== null) {
    await validateConnectedProject(client, connection.projectRoot);
  }
}
async function waitForDynamicCodeReadyAfterLaunch(projectPath, dependencies = defaultDependencies2) {
  const startTime = dependencies.nowFn();
  const totalProbeStageCount = LAUNCH_READINESS_REQUIRED_STABLE_PROBE_COUNT + 1;
  const isProjectBusyFn = dependencies.isProjectBusyFn ?? isProjectBusyByLockFiles;
  let currentProbeStage = 0;
  let firstSuccessfulProbeTime = null;
  let probeSessionId = null;
  while (dependencies.nowFn() - startTime < LAUNCH_READINESS_TIMEOUT_MS) {
    let client = null;
    try {
      const connection = await dependencies.resolveUnityConnectionFn(
        void 0,
        projectPath
      );
      if (!hasFastSessionMetadata(connection)) {
        if (probeSessionId !== null) {
          currentProbeStage = 0;
          firstSuccessfulProbeTime = null;
        }
        probeSessionId = null;
      } else {
        const resolvedSessionId = connection.requestMetadata?.expectedServerSessionId ?? null;
        if (probeSessionId !== null && probeSessionId !== resolvedSessionId) {
          currentProbeStage = 0;
          firstSuccessfulProbeTime = null;
        }
        probeSessionId = resolvedSessionId;
      }
      client = dependencies.createClient(connection.port);
      await client.connect();
      if (!hasFastSessionMetadata(connection) && connection.projectRoot !== null) {
        await validateConnectedProject(client, connection.projectRoot);
      } else {
        probeSessionId = connection.requestMetadata?.expectedServerSessionId ?? null;
      }
      if (currentProbeStage >= totalProbeStageCount) {
        if (!isProjectBusyFn(projectPath)) {
          return connection;
        }
        await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
        continue;
      }
      const payload = await client.sendRequest(
        "execute-dynamic-code",
        {
          Code: currentProbeStage < LAUNCH_READINESS_REQUIRED_STABLE_PROBE_COUNT ? LAUNCH_READINESS_STABLE_CODE : LAUNCH_READINESS_USER_LIKE_CODE,
          CompileOnly: false,
          YieldToForegroundRequests: true
        },
        {
          requestMetadata: connection.requestMetadata ?? void 0
        }
      );
      const isMalformedPayload = payload === void 0 || payload === null || typeof payload.Success !== "boolean";
      if (!isMalformedPayload) {
        if (payload.Success) {
          if (isLaunchReadinessStable(payload)) {
            currentProbeStage++;
            firstSuccessfulProbeTime = null;
            if (currentProbeStage >= totalProbeStageCount && !isProjectBusyFn(projectPath)) {
              return connection;
            }
          } else {
            if (firstSuccessfulProbeTime === null) {
              firstSuccessfulProbeTime = dependencies.nowFn();
            } else if (dependencies.nowFn() - firstSuccessfulProbeTime >= LAUNCH_READINESS_SETTLE_TIMEOUT_MS) {
              currentProbeStage++;
              firstSuccessfulProbeTime = null;
              if (currentProbeStage >= totalProbeStageCount && !isProjectBusyFn(projectPath)) {
                return connection;
              }
            }
          }
        } else {
          currentProbeStage = 0;
          firstSuccessfulProbeTime = null;
          if (!isTransientExecuteDynamicCodeFailure(payload)) {
            throw createLaunchReadinessFailure(payload);
          }
        }
      } else {
        currentProbeStage = 0;
        firstSuccessfulProbeTime = null;
      }
    } catch (error) {
      if (!isRetryableLaunchReadinessError(error)) {
        throw error;
      }
      currentProbeStage = 0;
      firstSuccessfulProbeTime = null;
    } finally {
      client?.disconnect();
    }
    await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
  }
  throw new Error(
    `Timed out waiting for execute-dynamic-code to become ready after launch (${LAUNCH_READINESS_TIMEOUT_MS}ms).`
  );
}
async function waitForLaunchReadyAfterLaunch(projectPath, dependencies = defaultDependencies2) {
  const startTime = dependencies.nowFn();
  const isProjectBusyFn = dependencies.isProjectBusyFn ?? isProjectBusyByLockFiles;
  while (dependencies.nowFn() - startTime < LAUNCH_READINESS_TIMEOUT_MS) {
    let client = null;
    try {
      const connection = await dependencies.resolveUnityConnectionFn(
        void 0,
        projectPath
      );
      client = dependencies.createClient(connection.port);
      await client.connect();
      await validateLaunchConnectionIdentity(client, connection);
      if (!isProjectBusyFn(projectPath)) {
        return connection;
      }
    } catch (error) {
      if (!isRetryableLaunchReadinessError(error)) {
        throw error;
      }
    } finally {
      client?.disconnect();
    }
    await dependencies.sleepFn(LAUNCH_READINESS_RETRY_MS);
  }
  throw new Error(
    `Timed out waiting for Unity to finish launch readiness after launch (${LAUNCH_READINESS_TIMEOUT_MS}ms).`
  );
}

// src/launch-restart-guard.ts
var import_node_assert5 = __toESM(require("node:assert"), 1);
var import_node_fs3 = require("node:fs");
var import_node_path3 = require("node:path");
var UNITY_RESTART_GUARD_COOLDOWN_MS = 12e4;
var UNITY_RESTART_GUARD_DIR = ".uloop";
var UNITY_RESTART_GUARD_FILE = "launch-restart-guard.json";
var defaultDependencies3 = {
  mkdirSyncFn: import_node_fs3.mkdirSync,
  readFileSyncFn: import_node_fs3.readFileSync,
  writeFileSyncFn: import_node_fs3.writeFileSync,
  nowFn: () => Date.now(),
  pid: process.pid
};
var UnityRestartCooldownError = class extends Error {
  constructor(projectPath, remainingMilliseconds) {
    const remainingSeconds = Math.ceil(remainingMilliseconds / 1e3);
    super(
      `Refusing to restart Unity for ${projectPath} because a restart was requested recently. Wait ${remainingSeconds}s before retrying, or launch without --restart.`
    );
    this.name = "UnityRestartCooldownError";
  }
};
function getUnityRestartGuardFilePath(projectPath) {
  (0, import_node_assert5.default)(projectPath.length > 0, "projectPath must not be empty");
  return (0, import_node_path3.join)(projectPath, UNITY_RESTART_GUARD_DIR, UNITY_RESTART_GUARD_FILE);
}
function beginUnityRestartAttempt(projectPath, dependencies = defaultDependencies3) {
  (0, import_node_assert5.default)(projectPath.length > 0, "projectPath must not be empty");
  assertUnityRestartAllowed(projectPath, dependencies);
  const guardPath = getUnityRestartGuardFilePath(projectPath);
  dependencies.mkdirSyncFn((0, import_node_path3.dirname)(guardPath), { recursive: true });
  const record = {
    projectPath,
    startedAt: dependencies.nowFn(),
    pid: dependencies.pid
  };
  dependencies.writeFileSyncFn(guardPath, `${JSON.stringify(record, null, 2)}
`, "utf8");
}
function assertUnityRestartAllowed(projectPath, dependencies = defaultDependencies3) {
  (0, import_node_assert5.default)(projectPath.length > 0, "projectPath must not be empty");
  const guardPath = getUnityRestartGuardFilePath(projectPath);
  const record = readGuardRecordOrNull(guardPath, dependencies);
  if (record === null) {
    return;
  }
  const elapsedMilliseconds = dependencies.nowFn() - record.startedAt;
  if (elapsedMilliseconds < 0 || elapsedMilliseconds >= UNITY_RESTART_GUARD_COOLDOWN_MS) {
    return;
  }
  throw new UnityRestartCooldownError(
    projectPath,
    UNITY_RESTART_GUARD_COOLDOWN_MS - elapsedMilliseconds
  );
}
function readGuardRecordOrNull(guardPath, dependencies) {
  try {
    const content = dependencies.readFileSyncFn(guardPath, "utf8");
    const parsed = JSON.parse(content);
    (0, import_node_assert5.default)(typeof parsed.projectPath === "string", "restart guard projectPath must be a string");
    (0, import_node_assert5.default)(typeof parsed.startedAt === "number", "restart guard startedAt must be a number");
    (0, import_node_assert5.default)(typeof parsed.pid === "number", "restart guard pid must be a number");
    return {
      projectPath: parsed.projectPath,
      startedAt: parsed.startedAt,
      pid: parsed.pid
    };
  } catch {
    return null;
  }
}

// src/commands/launch.ts
function registerLaunchCommand(program2) {
  program2.command("launch").description(
    "Open a Unity project with the matching Editor version installed by Unity Hub.\nAuto-detects project path and Unity version from ProjectSettings/ProjectVersion.txt.\nRun 'uloop launch -h' for all options. Details: https://github.com/hatayama/LaunchUnityCommand"
  ).argument("[project-path]", "Path to Unity project").option("-r, --restart", "Kill running Unity and restart").option("-d, --delete-recovery", "Delete Assets/_Recovery before launch").option("-q, --quit", "Gracefully quit running Unity").option("-p, --platform <platform>", "Build target (e.g., Android, iOS)").option("--max-depth <n>", "Search depth when project-path is omitted", "3").option("-a, --add-unity-hub", "Add to Unity Hub (does not launch)").option("-f, --favorite", "Add to Unity Hub as favorite (does not launch)").action(async (projectPath, options) => {
    await runLaunchCommand(projectPath, options);
  });
}
function parseMaxDepth(value) {
  if (value === void 0) {
    return 3;
  }
  const parsed = parseInt(value, 10);
  if (Number.isNaN(parsed)) {
    console.error(`Error: Invalid --max-depth value: "${value}". Must be an integer.`);
    process.exit(1);
  }
  return parsed;
}
async function runLaunchCommand(projectPath, options) {
  const maxDepth = parseMaxDepth(options.maxDepth);
  const resolvedProjectPath = projectPath ? (0, import_path10.resolve)(projectPath) : void 0;
  if (options.restart === true) {
    const restartGuardProjectPath = resolveRestartGuardProjectPath(resolvedProjectPath);
    if (restartGuardProjectPath !== null) {
      beginUnityRestartAttempt(restartGuardProjectPath);
    }
  }
  const spinner = createSpinner("Waiting for Unity to finish starting...", "stdout");
  try {
    const launchResult = await orchestrateLaunch({
      projectPath: resolvedProjectPath,
      searchRoot: process.cwd(),
      searchMaxDepth: maxDepth,
      platform: options.platform,
      unityArgs: [],
      restart: options.restart === true,
      quit: options.quit === true,
      deleteRecovery: options.deleteRecovery === true,
      addUnityHub: options.addUnityHub === true,
      favoriteUnityHub: options.favorite === true
    });
    if (launchResult.action !== "launched" && launchResult.action !== "killed-and-launched") {
      return;
    }
    const isDynamicCodeEnabled = isToolEnabled(
      "execute-dynamic-code",
      launchResult.projectPath
    );
    if (!isDynamicCodeEnabled) {
      await waitForLaunchReadyAfterLaunch(launchResult.projectPath);
      return;
    }
    const readinessConnection = await waitForDynamicCodeReadyAfterLaunch(
      launchResult.projectPath
    );
    await prewarmDynamicCodeAfterLaunch({ port: readinessConnection.port });
  } finally {
    spinner.stop();
  }
}
function resolveRestartGuardProjectPath(resolvedProjectPath) {
  if (resolvedProjectPath === void 0) {
    return findUnityProjectRoot(process.cwd());
  }
  if (!isUnityProject(resolvedProjectPath)) {
    return null;
  }
  return resolvedProjectPath;
}

// src/commands/focus-window.ts
var import_fs9 = require("fs");
var import_path11 = require("path");
function registerFocusWindowCommand(program2, helpGroup) {
  const cmd = program2.command("focus-window").description("Bring Unity Editor window to front using OS-level commands").option("--project-path <path>", "Unity project path");
  if (helpGroup !== void 0) {
    cmd.helpGroup(helpGroup);
  }
  cmd.action(async (options) => {
    const exitCode = await runFocusWindowCommand(options);
    if (exitCode !== 0) {
      process.exit(exitCode);
    }
  });
}
async function runFocusWindowCommand(options, output = console) {
  const projectRootResult = resolveFocusProjectRoot(options.projectPath);
  if (projectRootResult.error !== null) {
    writeFocusWindowError(output, projectRootResult.error);
    return 1;
  }
  const runningProcess = await findRunningUnityProcess(projectRootResult.projectRoot);
  if (!runningProcess) {
    writeFocusWindowError(output, "No running Unity process found for this project");
    return 1;
  }
  try {
    await focusUnityProcess(runningProcess.pid);
    output.log(
      JSON.stringify({
        Success: true,
        Message: `Unity Editor window focused (PID: ${runningProcess.pid})`
      })
    );
    return 0;
  } catch (error) {
    writeFocusWindowError(
      output,
      `Failed to focus Unity window: ${error instanceof Error ? error.message : String(error)}`
    );
    return 1;
  }
}
function resolveFocusProjectRoot(projectPath) {
  if (projectPath !== void 0) {
    const resolvedProjectPath = (0, import_path11.resolve)(projectPath);
    if (!(0, import_fs9.existsSync)(resolvedProjectPath)) {
      return { projectRoot: null, error: `Path does not exist: ${resolvedProjectPath}` };
    }
    if (!isUnityProject(resolvedProjectPath)) {
      return {
        projectRoot: null,
        error: `Not a Unity project (Assets/ or ProjectSettings/ not found): ${resolvedProjectPath}`
      };
    }
    return { projectRoot: resolvedProjectPath, error: null };
  }
  const projectStatus = getUnityProjectStatus();
  if (!projectStatus.found || projectStatus.path === null) {
    return { projectRoot: null, error: "Unity project not found" };
  }
  return { projectRoot: projectStatus.path, error: null };
}
function writeFocusWindowError(output, message) {
  output.error(
    JSON.stringify({
      Success: false,
      Message: message
    })
  );
}

// src/cli-project-error.ts
function getProjectResolutionErrorLines(error) {
  if (error instanceof UnityServerNotRunningError) {
    return [
      "Error: Unity Editor is running, but Unity CLI Loop server is not.",
      "",
      `  Project: ${error.projectRoot}`,
      "",
      "Start the server from: Window > Unity CLI Loop > Server"
    ];
  }
  if (error instanceof UnityNotRunningError) {
    return [
      "Error: Unity Editor for this project is not running.",
      "",
      `  Project: ${error.projectRoot}`,
      "",
      "Start the Unity Editor for this project and try again."
    ];
  }
  return [
    "Error: Connected Unity instance belongs to a different project.",
    "",
    `  Project:      ${error.expectedProjectRoot}`,
    `  Connected to: ${error.connectedProjectRoot}`,
    "",
    "Another Unity instance was found, but it belongs to a different project.",
    "Start the Unity Editor for this project, or use --project-path to specify the target."
  ];
}

// src/cli.ts
var FOCUS_WINDOW_COMMAND = "focus-window";
var LAUNCH_COMMAND = "launch";
var UPDATE_COMMAND = "update";
var PROJECT_LOCAL_CLI_IN_PROCESS_MARKER = "uloop-cli-in-process-entrypoint-v2";
var HELP_GROUP_BUILTIN_TOOLS = "Built-in Tools:";
var HELP_GROUP_THIRD_PARTY_TOOLS = "Third-party Tools:";
var HELP_GROUP_CLI_COMMANDS = "CLI Commands:";
var HELP_GROUP_ORDER = [
  HELP_GROUP_CLI_COMMANDS,
  HELP_GROUP_BUILTIN_TOOLS,
  HELP_GROUP_THIRD_PARTY_TOOLS
];
var NO_SYNC_FLAGS = ["-v", "--version", "-h", "--help"];
var BUILTIN_COMMANDS = [
  "list",
  "sync",
  "completion",
  UPDATE_COMMAND,
  "fix",
  "skills",
  LAUNCH_COMMAND,
  FOCUS_WINDOW_COMMAND
];
function registerToolCommand(program2, tool, helpGroup) {
  if (BUILTIN_COMMANDS.includes(tool.name)) {
    return;
  }
  const firstLine = tool.description.split("\n")[0];
  const cmd = program2.command(tool.name).description(firstLine).helpGroup(helpGroup);
  const properties = tool.inputSchema.properties;
  for (const [propName, propInfo] of Object.entries(properties)) {
    const optionStr = generateOptionString(propName, propInfo);
    const description = buildOptionDescription(propInfo);
    const defaultValue = propInfo.default;
    if (defaultValue !== void 0 && defaultValue !== null) {
      const defaultStr = convertDefaultToString(defaultValue);
      cmd.option(optionStr, description, defaultStr);
    } else {
      cmd.option(optionStr, description);
    }
  }
  cmd.addOption(createHiddenPortOption());
  cmd.option("--project-path <path>", "Unity project path");
  cmd.action(async (options) => {
    const params = buildParams(options, properties);
    if (tool.name === "execute-dynamic-code" && params["Code"]) {
      const code = params["Code"];
      params["Code"] = code.replace(/\\!/g, "!");
    }
    await runWithErrorHandling(
      () => executeToolCommand(tool.name, params, extractGlobalOptions(options))
    );
  });
}
function convertDefaultToString(value) {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "boolean" || typeof value === "number") {
    return String(value);
  }
  return JSON.stringify(value);
}
function generateOptionString(propName, propInfo) {
  const kebabName = pascalToKebabCase(propName);
  void propInfo;
  return `--${kebabName} <value>`;
}
function buildOptionDescription(propInfo) {
  let desc = propInfo.description || "";
  if (propInfo.enum && propInfo.enum.length > 0) {
    desc += ` (${propInfo.enum.join(", ")})`;
  }
  return desc;
}
function buildParams(options, properties) {
  const params = {};
  for (const propName of Object.keys(properties)) {
    const camelName = propName.charAt(0).toLowerCase() + propName.slice(1);
    const value = options[camelName];
    if (value !== void 0) {
      const propInfo = properties[propName];
      params[propName] = convertValue(value, propInfo);
    }
  }
  return params;
}
function convertValue(value, propInfo) {
  const lowerType = propInfo.type.toLowerCase();
  if (lowerType === "boolean" && typeof value === "string") {
    const lower = value.toLowerCase();
    if (lower === "true") {
      return true;
    }
    if (lower === "false") {
      return false;
    }
    throw new Error(`Invalid boolean value: ${value}. Use 'true' or 'false'.`);
  }
  if (lowerType === "array" && typeof value === "string") {
    if (value.startsWith("[") && value.endsWith("]")) {
      try {
        const parsed = JSON.parse(value);
        if (Array.isArray(parsed)) {
          return parsed;
        }
      } catch {
      }
    }
    return value.split(",").map((s) => s.trim());
  }
  if (lowerType === "integer" && typeof value === "string") {
    const parsed = parseInt(value, 10);
    if (isNaN(parsed)) {
      throw new Error(`Invalid integer value: ${value}`);
    }
    return parsed;
  }
  if (lowerType === "number" && typeof value === "string") {
    const parsed = parseFloat(value);
    if (isNaN(parsed)) {
      throw new Error(`Invalid number value: ${value}`);
    }
    return parsed;
  }
  if (lowerType === "object") {
    if (typeof value === "string") {
      const trimmed = value.trim();
      if (!trimmed.startsWith("{") || !trimmed.endsWith("}")) {
        throw new Error(`Invalid object value: ${value}. Use JSON object syntax.`);
      }
      try {
        const parsed = JSON.parse(trimmed);
        if (typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)) {
          return parsed;
        }
      } catch {
      }
      throw new Error(`Invalid object value: ${value}. Use JSON object syntax.`);
    }
    if (typeof value === "object" && value !== null && !Array.isArray(value)) {
      return value;
    }
    throw new Error(`Invalid object value: ${String(value)}. Use JSON object syntax.`);
  }
  return value;
}
function getToolHelpGroup(toolName, defaultToolNames) {
  return defaultToolNames.has(toolName) ? HELP_GROUP_BUILTIN_TOOLS : HELP_GROUP_THIRD_PARTY_TOOLS;
}
function createHiddenPortOption() {
  return new Option("-p, --port <port>", "Unity TCP port").hideHelp();
}
function createProgram() {
  const program2 = new Command();
  program2.name("uloop").description("Unity CLI Loop - Direct communication with Unity Editor").version(VERSION, "-v, --version", "Output the version number").showHelpAfterError("(run with -h for available options)").configureHelp({
    sortSubcommands: true,
    groupItems(unsortedItems, visibleItems, getGroup) {
      const groupMap = /* @__PURE__ */ new Map();
      for (const item of unsortedItems) {
        const group = getGroup(item);
        if (!groupMap.has(group)) {
          groupMap.set(group, []);
        }
      }
      for (const item of visibleItems) {
        const group = getGroup(item);
        let groupedItems = groupMap.get(group);
        if (groupedItems === void 0) {
          groupedItems = [];
          groupMap.set(group, groupedItems);
        }
        groupedItems.push(item);
      }
      const ordered = /* @__PURE__ */ new Map();
      for (const key of HELP_GROUP_ORDER) {
        const items = groupMap.get(key);
        if (items !== void 0) {
          ordered.set(key, items);
          groupMap.delete(key);
        }
      }
      for (const [key, value] of groupMap) {
        ordered.set(key, value);
      }
      return ordered;
    }
  });
  program2.option("--list-commands", "List all command names (for shell completion)");
  program2.option("--list-options <cmd>", "List options for a command (for shell completion)");
  program2.commandsGroup(HELP_GROUP_CLI_COMMANDS);
  program2.helpCommand(true);
  program2.command("list").description("List all available tools from Unity").addOption(createHiddenPortOption()).option("--project-path <path>", "Unity project path").action(async (options) => {
    await runWithErrorHandling(() => listAvailableTools(extractGlobalOptions(options)));
  });
  program2.command("sync").description("Sync tool definitions from Unity to local cache").addOption(createHiddenPortOption()).option("--project-path <path>", "Unity project path").action(async (options) => {
    await runWithErrorHandling(() => syncTools(extractGlobalOptions(options)));
  });
  program2.command("completion").description("Setup shell completion").option("--install", "Install completion to shell config file").option("--shell <type>", "Shell type: bash, zsh, or powershell").action((options) => {
    handleCompletion(options.install ?? false, options.shell);
  });
  program2.command("update").description("Update uloop CLI to the latest version").action(() => {
    updateCli();
  });
  program2.command("fix").description("Clean up stale lock files that may prevent CLI from connecting").option("--project-path <path>", "Unity project path").action(async (options) => {
    await runWithErrorHandling(() => {
      cleanupLockFiles(options.projectPath);
      return Promise.resolve();
    });
  });
  registerSkillsCommand(program2);
  registerLaunchCommand(program2);
  return program2;
}
var defaultFastExecuteDynamicCodeDependencies = {
  executeToolCommandFn: executeToolCommand,
  isToolEnabledFn: isToolEnabled,
  findUnityProjectRootFn: findUnityProjectRoot,
  runWithErrorHandlingFn: runWithErrorHandling,
  printToolDisabledErrorFn: printToolDisabledError,
  exitFn: (code) => process.exit(code)
};
var EXECUTE_DYNAMIC_CODE_PROPERTIES = getDefaultTools().tools.find((tool) => tool.name === "execute-dynamic-code")?.inputSchema.properties ?? {};
var FAST_EXECUTE_DYNAMIC_CODE_OPTIONS = /* @__PURE__ */ new Map([
  ["--code", "code"],
  ["--parameters", "parameters"],
  ["--compile-only", "compileOnly"],
  ["--yield-to-foreground-requests", "yieldToForegroundRequests"],
  ["--project-path", "projectPath"],
  ["--port", "port"],
  ["-p", "port"]
]);
function parseFastOptionValue(arg) {
  const separatorIndex = arg.indexOf("=");
  if (separatorIndex === -1) {
    return null;
  }
  const optionName = arg.slice(0, separatorIndex);
  const optionValue = arg.slice(separatorIndex + 1);
  if (!FAST_EXECUTE_DYNAMIC_CODE_OPTIONS.has(optionName)) {
    return null;
  }
  return [optionName, optionValue];
}
function tryParseFastExecuteDynamicCodeCommand(args) {
  if (args[0] !== "execute-dynamic-code") {
    return null;
  }
  if (args.includes("-h") || args.includes("--help")) {
    return null;
  }
  const options = {};
  for (let i = 1; i < args.length; i++) {
    const arg = args[i];
    if (!arg.startsWith("-")) {
      return null;
    }
    const inlineOption = parseFastOptionValue(arg);
    if (inlineOption !== null) {
      const [optionName, optionValue2] = inlineOption;
      const optionKey2 = FAST_EXECUTE_DYNAMIC_CODE_OPTIONS.get(optionName);
      if (optionKey2 === void 0) {
        return null;
      }
      options[optionKey2] = optionValue2;
      continue;
    }
    const optionKey = FAST_EXECUTE_DYNAMIC_CODE_OPTIONS.get(arg);
    if (optionKey === void 0) {
      return null;
    }
    const optionValue = args[i + 1];
    if (optionValue === void 0) {
      return null;
    }
    options[optionKey] = optionValue;
    i++;
  }
  if (typeof options["code"] !== "string") {
    return null;
  }
  const params = buildParams(options, EXECUTE_DYNAMIC_CODE_PROPERTIES);
  const code = params["Code"];
  if (typeof code === "string") {
    params["Code"] = code.replace(/\\!/g, "!");
  }
  return {
    params,
    globalOptions: extractGlobalOptions(options)
  };
}
async function tryHandleFastExecuteDynamicCodeCommand(args, dependencies = defaultFastExecuteDynamicCodeDependencies) {
  const command = tryParseFastExecuteDynamicCodeCommand(args);
  if (command === null) {
    return false;
  }
  const resolvedProjectPath = command.globalOptions.projectPath !== void 0 || command.globalOptions.port !== void 0 ? command.globalOptions.projectPath : dependencies.findUnityProjectRootFn() ?? void 0;
  const resolvedGlobalOptions = {
    ...command.globalOptions,
    projectPath: resolvedProjectPath
  };
  if (!dependencies.isToolEnabledFn("execute-dynamic-code", resolvedGlobalOptions.projectPath)) {
    dependencies.printToolDisabledErrorFn("execute-dynamic-code");
    dependencies.exitFn(1);
  }
  await dependencies.runWithErrorHandlingFn(
    () => dependencies.executeToolCommandFn(
      "execute-dynamic-code",
      command.params,
      resolvedGlobalOptions
    )
  );
  return true;
}
function extractGlobalOptions(options) {
  return {
    port: options["port"],
    projectPath: options["projectPath"]
  };
}
function isConnectionError(message) {
  return message.includes("ECONNREFUSED") || message.includes("EADDRNOTAVAIL");
}
function printToolDisabledError(cmdName) {
  console.error(`\x1B[33mTool '${cmdName}' is disabled.\x1B[0m`);
  console.error("You can enable it in Unity: Window > Unity CLI Loop > Settings > Tool Settings");
}
function printConnectionError() {
  console.error("\x1B[31mError: Cannot connect to Unity.\x1B[0m");
  console.error(
    `Make sure Unity Editor is open and the ${PRODUCT_DISPLAY_NAME} bridge is running.`
  );
  console.error(`You can start the server from: ${MENU_PATH_SERVER}`);
  console.error("");
  console.error("[For AI] Please report the above to the user.");
}
function printVersionMismatchDiagnostic() {
  const serverVersion = getCachedServerVersion();
  if (serverVersion === void 0) {
    return;
  }
  if (serverVersion === VERSION) {
    return;
  }
  console.error("");
  console.error("\x1B[33m\u2501\u2501\u2501 Version Mismatch Diagnostic \u2501\u2501\u2501\x1B[0m");
  console.error("");
  console.error(`  Project CLI:     ${VERSION}`);
  console.error(`  Unity package:   ${serverVersion}`);
  console.error("  Version mismatch may cause communication errors.");
  console.error("");
  console.error(
    "  To fix: reopen Unity or reload Unity CLI Loop so the package can refresh the project-local CLI."
  );
  console.error("");
  console.error(
    "[For AI] Please report the mismatch and ask the user to refresh the CLI in Unity."
  );
  console.error("\x1B[33m\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\u2501\x1B[0m");
}
async function runWithErrorHandling(fn) {
  try {
    await fn();
  } catch (error) {
    if (error instanceof UnityNotRunningError || error instanceof UnityServerNotRunningError) {
      for (const line of getProjectResolutionErrorLines(error)) {
        console.error(line.startsWith("Error: ") ? `\x1B[31m${line}\x1B[0m` : line);
      }
      process.exit(1);
    }
    if (error instanceof ProjectMismatchError) {
      for (const line of getProjectResolutionErrorLines(error)) {
        console.error(line.startsWith("Error: ") ? `\x1B[31m${line}\x1B[0m` : line);
      }
      process.exit(1);
    }
    const message = error instanceof Error ? error.message : String(error);
    if (message === "UNITY_COMPILING") {
      console.error("\x1B[33m\u23F3 Unity is compiling scripts.\x1B[0m");
      console.error("Please wait for compilation to finish and try again.");
      console.error("");
      console.error("If the issue persists, run: uloop fix");
      process.exit(1);
    }
    if (message === "UNITY_DOMAIN_RELOAD") {
      console.error("\x1B[33m\u23F3 Unity is reloading (Domain Reload in progress).\x1B[0m");
      console.error("Please wait a moment and try again.");
      console.error("");
      console.error("If the issue persists, run: uloop fix");
      process.exit(1);
    }
    if (message === "UNITY_SERVER_STARTING") {
      console.error("\x1B[33m\u23F3 Unity server is starting.\x1B[0m");
      console.error("Please wait a moment and try again.");
      console.error("");
      console.error("If the issue persists, run: uloop fix");
      process.exit(1);
    }
    if (message === "UNITY_NO_RESPONSE") {
      console.error("\x1B[33m\u23F3 Unity is busy (no response received).\x1B[0m");
      console.error("Unity may be compiling, reloading, or starting. Please wait and try again.");
      printVersionMismatchDiagnostic();
      process.exit(1);
    }
    if (isConnectionError(message)) {
      printConnectionError();
      printVersionMismatchDiagnostic();
      process.exit(1);
    }
    if (message.includes("Request timed out")) {
      console.error(`\x1B[31mError: ${message}\x1B[0m`);
      printVersionMismatchDiagnostic();
      process.exit(1);
    }
    console.error(`\x1B[31mError: ${message}\x1B[0m`);
    process.exit(1);
  }
}
function detectShell() {
  const shell = process.env["SHELL"] || "";
  const shellName = (0, import_path12.basename)(shell).replace(/\.exe$/i, "");
  if (shellName === "zsh") {
    return "zsh";
  }
  if (shellName === "bash") {
    return "bash";
  }
  if (process.env["PSModulePath"]) {
    return "powershell";
  }
  return null;
}
function getShellConfigPath(shell) {
  const home = (0, import_os2.homedir)();
  if (shell === "zsh") {
    return (0, import_path12.join)(home, ".zshrc");
  }
  if (shell === "powershell") {
    return (0, import_path12.join)(home, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
  }
  return (0, import_path12.join)(home, ".bashrc");
}
function getCompletionScript(shell) {
  if (shell === "bash") {
    return `# uloop bash completion
_uloop_completions() {
  local cur="\${COMP_WORDS[COMP_CWORD]}"
  local cmd="\${COMP_WORDS[1]}"

  if [[ \${COMP_CWORD} -eq 1 ]]; then
    COMPREPLY=($(compgen -W "$(uloop --list-commands 2>/dev/null)" -- "\${cur}"))
  elif [[ \${COMP_CWORD} -ge 2 ]]; then
    COMPREPLY=($(compgen -W "$(uloop --list-options \${cmd} 2>/dev/null)" -- "\${cur}"))
  fi
}
complete -F _uloop_completions uloop`;
  }
  if (shell === "powershell") {
    return `# uloop PowerShell completion
Register-ArgumentCompleter -Native -CommandName uloop -ScriptBlock {
  param($wordToComplete, $commandAst, $cursorPosition)
  $commands = $commandAst.CommandElements
  if ($commands.Count -eq 1) {
    uloop --list-commands 2>$null | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
      [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
  } elseif ($commands.Count -ge 2) {
    $cmd = $commands[1].ToString()
    uloop --list-options $cmd 2>$null | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
      [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
  }
}`;
  }
  return `# uloop zsh completion
_uloop() {
  local -a commands
  local -a options
  local -a used_options

  if (( CURRENT == 2 )); then
    commands=(\${(f)"$(uloop --list-commands 2>/dev/null)"})
    _describe 'command' commands
  elif (( CURRENT >= 3 )); then
    options=(\${(f)"$(uloop --list-options \${words[2]} 2>/dev/null)"})
    used_options=(\${words:2})
    for opt in \${used_options}; do
      options=(\${options:#$opt})
    done
    _describe 'option' options
  fi
}
compdef _uloop uloop`;
}
function getInstalledVersion(callback) {
  const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";
  const child = (0, import_child_process2.spawn)(npmCommand, ["list", "-g", "uloop-cli", "--json"]);
  let stdout = "";
  child.stdout.on("data", (data) => {
    stdout += data.toString();
  });
  child.on("close", (code) => {
    if (code !== 0) {
      callback(null);
      return;
    }
    let parsed;
    try {
      parsed = JSON.parse(stdout);
    } catch {
      callback(null);
      return;
    }
    if (typeof parsed !== "object" || parsed === null) {
      callback(null);
      return;
    }
    const deps = parsed["dependencies"];
    if (typeof deps !== "object" || deps === null) {
      callback(null);
      return;
    }
    const uloopCli = deps["uloop-cli"];
    if (typeof uloopCli !== "object" || uloopCli === null) {
      callback(null);
      return;
    }
    const version = uloopCli["version"];
    if (typeof version !== "string") {
      callback(null);
      return;
    }
    callback(version);
  });
  child.on("error", () => {
    callback(null);
  });
}
function getUpdatePackageSpec(version = VERSION) {
  const prereleasePrefixIndex = version.indexOf("-");
  if (prereleasePrefixIndex === -1) {
    return "uloop-cli@latest";
  }
  const prerelease = version.slice(prereleasePrefixIndex + 1);
  const channel = prerelease.split(".")[0];
  return channel.length > 0 ? `uloop-cli@${channel}` : "uloop-cli@latest";
}
function updateCli() {
  const previousVersion = VERSION;
  console.log("Updating global uloop dispatcher to the latest version...");
  const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";
  const child = (0, import_child_process2.spawn)(npmCommand, ["install", "-g", getUpdatePackageSpec()], {
    stdio: "inherit"
  });
  child.on("close", (code) => {
    if (code === 0) {
      getInstalledVersion((newVersion) => {
        if (newVersion && newVersion !== previousVersion) {
          console.log(`
\u2705 uloop-cli updated: v${previousVersion} -> v${newVersion}`);
        } else {
          console.log(`
\u2705 Already up to date (v${previousVersion})`);
        }
        console.log(
          "Unity CLI Loop refreshes the project-local CLI automatically inside each Unity project."
        );
      });
    } else {
      console.error(`
\u274C Update failed with exit code ${code}`);
      process.exit(1);
    }
  });
  child.on("error", (err) => {
    console.error(`\u274C Failed to run npm: ${err.message}`);
    process.exit(1);
  });
}
var LOCK_FILES = ["compiling.lock", "domainreload.lock", "serverstarting.lock"];
function cleanupLockFiles(projectPath) {
  const projectRoot = projectPath !== void 0 ? validateProjectPath(projectPath) : findUnityProjectRoot();
  if (projectRoot === null) {
    console.error("Could not find Unity project root.");
    process.exit(1);
  }
  const tempDir = (0, import_path12.join)(projectRoot, "Temp");
  let cleaned = 0;
  for (const lockFile of LOCK_FILES) {
    const lockPath = (0, import_path12.join)(tempDir, lockFile);
    if ((0, import_fs10.existsSync)(lockPath)) {
      (0, import_fs10.unlinkSync)(lockPath);
      console.log(`Removed: ${lockFile}`);
      cleaned++;
    }
  }
  if (cleaned === 0) {
    console.log("No lock files found.");
  } else {
    console.log(`
\u2705 Cleaned up ${cleaned} lock file(s).`);
  }
}
function handleCompletion(install, shellOverride) {
  let shell;
  if (shellOverride) {
    const normalized = shellOverride.toLowerCase();
    if (normalized === "bash" || normalized === "zsh" || normalized === "powershell") {
      shell = normalized;
    } else {
      console.error(`Unknown shell: ${shellOverride}. Supported: bash, zsh, powershell`);
      process.exit(1);
    }
  } else {
    shell = detectShell();
  }
  if (!shell) {
    console.error("Could not detect shell. Use --shell option: bash, zsh, or powershell");
    process.exit(1);
  }
  const script = getCompletionScript(shell);
  if (!install) {
    console.log(script);
    return;
  }
  const configPath = getShellConfigPath(shell);
  const configDir = (0, import_path12.dirname)(configPath);
  if (!(0, import_fs10.existsSync)(configDir)) {
    (0, import_fs10.mkdirSync)(configDir, { recursive: true });
  }
  let content = "";
  if ((0, import_fs10.existsSync)(configPath)) {
    content = (0, import_fs10.readFileSync)(configPath, "utf-8");
    content = content.replace(
      /\n?# >>> uloop completion >>>[\s\S]*?# <<< uloop completion <<<\n?/g,
      ""
    );
  }
  const startMarker = "# >>> uloop completion >>>";
  const endMarker = "# <<< uloop completion <<<";
  if (shell === "powershell") {
    const lineToAdd = `
${startMarker}
${script}
${endMarker}
`;
    (0, import_fs10.writeFileSync)(configPath, content + lineToAdd, "utf-8");
  } else {
    const evalLine = `eval "$(uloop completion --shell ${shell})"`;
    const lineToAdd = `
${startMarker}
${evalLine}
${endMarker}
`;
    (0, import_fs10.writeFileSync)(configPath, content + lineToAdd, "utf-8");
  }
  console.log(`Completion installed to ${configPath}`);
  if (shell === "powershell") {
    console.log("Restart PowerShell to enable completion.");
  } else {
    console.log(`Run 'source ${configPath}' or restart your shell to enable completion.`);
  }
}
function handleCompletionOptions(args = process.argv.slice(2)) {
  const projectPath = extractSyncGlobalOptions(args).projectPath;
  if (args.includes("--list-commands")) {
    const tools = loadToolsCache();
    const enabledTools = filterEnabledTools(tools.tools, projectPath);
    const allCommands = [
      ...BUILTIN_COMMANDS.filter(
        (cmd) => cmd !== FOCUS_WINDOW_COMMAND || isToolEnabled(cmd, projectPath)
      ),
      ...enabledTools.map((t) => t.name)
    ];
    console.log(allCommands.sort().join("\n"));
    return true;
  }
  const listOptionsIdx = args.indexOf("--list-options");
  if (listOptionsIdx !== -1 && args[listOptionsIdx + 1]) {
    const cmdName = args[listOptionsIdx + 1];
    listOptionsForCommand(cmdName, projectPath);
    return true;
  }
  return false;
}
function listOptionsForCommand(cmdName, projectPath) {
  if (BUILTIN_COMMANDS.includes(cmdName)) {
    return;
  }
  const tools = loadToolsCache();
  const tool = filterEnabledTools(tools.tools, projectPath).find(
    (t) => t.name === cmdName
  );
  if (!tool) {
    return;
  }
  const options = [];
  for (const propName of Object.keys(tool.inputSchema.properties)) {
    const kebabName = pascalToKebabCase(propName);
    options.push(`--${kebabName}`);
  }
  console.log(options.join("\n"));
}
function commandExists(cmdName, projectPath) {
  if (cmdName === FOCUS_WINDOW_COMMAND) {
    return isToolEnabled(FOCUS_WINDOW_COMMAND, projectPath);
  }
  if (BUILTIN_COMMANDS.includes(cmdName)) {
    return true;
  }
  const tools = loadToolsCache();
  return filterEnabledTools(tools.tools, projectPath).some((t) => t.name === cmdName);
}
function shouldSkipAutoSync(cmdName, args) {
  if (cmdName === LAUNCH_COMMAND || cmdName === UPDATE_COMMAND) {
    return true;
  }
  return args.some((arg) => NO_SYNC_FLAGS.includes(arg));
}
var OPTIONS_WITH_VALUE = /* @__PURE__ */ new Set(["--port", "-p", "--project-path"]);
function findCommandName(args) {
  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg.startsWith("-")) {
      if (OPTIONS_WITH_VALUE.has(arg)) {
        i++;
      }
      continue;
    }
    return arg;
  }
  return void 0;
}
function extractSyncGlobalOptions(args) {
  const options = {};
  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === "--port" || arg === "-p") {
      const nextArg = args[i + 1];
      if (nextArg !== void 0 && !nextArg.startsWith("-")) {
        options.port = nextArg;
      }
      continue;
    }
    if (arg.startsWith("--port=")) {
      options.port = arg.slice("--port=".length);
      continue;
    }
    if (arg === "--project-path") {
      const nextArg = args[i + 1];
      if (nextArg !== void 0 && !nextArg.startsWith("-")) {
        options.projectPath = nextArg;
      }
      continue;
    }
    if (arg.startsWith("--project-path=")) {
      options.projectPath = arg.slice("--project-path=".length);
      continue;
    }
  }
  return options;
}
async function main(args = process.argv.slice(2)) {
  if (handleCompletionOptions(args)) {
    return;
  }
  const program2 = createProgram();
  const syncGlobalOptions = extractSyncGlobalOptions(args);
  const cmdName = findCommandName(args);
  const parseArgs2 = ["node", "uloop", ...args];
  const NO_PROJECT_COMMANDS = [UPDATE_COMMAND, "completion"];
  const skipProjectDetection = cmdName === void 0 || NO_PROJECT_COMMANDS.includes(cmdName);
  if (skipProjectDetection) {
    const defaultToolNames2 = getDefaultToolNames();
    const isTopLevelHelp = cmdName === void 0 && (args.includes("-h") || args.includes("--help"));
    const shouldFilter = syncGlobalOptions.projectPath !== void 0 || isTopLevelHelp;
    const sourceTools = shouldFilter ? loadToolsCache(syncGlobalOptions.projectPath).tools : getDefaultTools().tools;
    const tools = shouldFilter ? filterEnabledTools(sourceTools, syncGlobalOptions.projectPath) : sourceTools;
    if (!shouldFilter || isToolEnabled(FOCUS_WINDOW_COMMAND, syncGlobalOptions.projectPath)) {
      registerFocusWindowCommand(program2, HELP_GROUP_BUILTIN_TOOLS);
    }
    for (const tool of tools) {
      registerToolCommand(program2, tool, getToolHelpGroup(tool.name, defaultToolNames2));
    }
    await program2.parseAsync(parseArgs2);
    return;
  }
  if (!shouldSkipAutoSync(cmdName, args)) {
    const cachedVersion = loadToolsCache().version;
    if (hasCacheFile() && cachedVersion !== VERSION) {
      console.log(
        `\x1B[33mCache outdated (${cachedVersion} \u2192 ${VERSION}). Syncing tools from Unity...\x1B[0m`
      );
      try {
        await syncTools(syncGlobalOptions);
        console.log("\x1B[32m\u2713 Tools synced successfully.\x1B[0m\n");
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        if (isConnectionError(message)) {
          console.error("\x1B[33mWarning: Failed to sync tools. Using cached definitions.\x1B[0m");
          console.error("\x1B[33mRun 'uloop sync' manually when Unity is available.\x1B[0m\n");
        } else {
          console.error("\x1B[33mWarning: Failed to sync tools. Using cached definitions.\x1B[0m");
          console.error(`\x1B[33mError: ${message}\x1B[0m`);
          console.error("\x1B[33mRun 'uloop sync' manually when Unity is available.\x1B[0m\n");
        }
      }
    }
  }
  const toolsCache = loadToolsCache();
  const projectPath = syncGlobalOptions.projectPath;
  const defaultToolNames = getDefaultToolNames();
  if (isToolEnabled(FOCUS_WINDOW_COMMAND, projectPath)) {
    registerFocusWindowCommand(program2, HELP_GROUP_BUILTIN_TOOLS);
  }
  for (const tool of filterEnabledTools(toolsCache.tools, projectPath)) {
    registerToolCommand(program2, tool, getToolHelpGroup(tool.name, defaultToolNames));
  }
  if (cmdName && !commandExists(cmdName, projectPath)) {
    if (!isToolEnabled(cmdName, projectPath)) {
      printToolDisabledError(cmdName);
      process.exit(1);
    }
    console.log(`\x1B[33mUnknown command '${cmdName}'. Syncing tools from Unity...\x1B[0m`);
    try {
      await syncTools(syncGlobalOptions);
      const newCache = loadToolsCache();
      const tool = filterEnabledTools(newCache.tools, projectPath).find((t) => t.name === cmdName);
      if (tool) {
        registerToolCommand(program2, tool, getToolHelpGroup(tool.name, defaultToolNames));
        console.log(`\x1B[32m\u2713 Found '${cmdName}' after sync.\x1B[0m
`);
      } else {
        console.error(`\x1B[31mError: Command '${cmdName}' not found even after sync.\x1B[0m`);
        process.exit(1);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (isConnectionError(message)) {
        printConnectionError();
      } else {
        console.error(`\x1B[31mError: Failed to sync tools: ${message}\x1B[0m`);
      }
      process.exit(1);
    }
  }
  await program2.parseAsync(parseArgs2);
}
async function runCli(args = process.argv.slice(2)) {
  const cliArgs = [...args];
  if (await tryHandleFastExecuteDynamicCodeCommand(cliArgs)) {
    return;
  }
  await main(cliArgs);
}
function shouldRunCliEntryPoint() {
  if (process.env["ULOOP_DISPATCHER_IN_PROCESS"] === "1") {
    return false;
  }
  if (process.env.JEST_WORKER_ID === void 0) {
    return true;
  }
  return (0, import_path12.basename)(process.argv[1] ?? "") === "cli.bundle.cjs";
}
if (shouldRunCliEntryPoint()) {
  const args = process.argv.slice(2);
  void runCli(args);
}
// Annotate the CommonJS export names for ESM import in node:
0 && (module.exports = {
  PROJECT_LOCAL_CLI_IN_PROCESS_MARKER,
  getInstalledVersion,
  getUpdatePackageSpec,
  runCli,
  tryHandleFastExecuteDynamicCodeCommand,
  tryParseFastExecuteDynamicCodeCommand,
  updateCli
});
//# sourceMappingURL=cli.bundle.cjs.map
