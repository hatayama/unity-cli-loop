package tools

type ToolCatalog struct {
	ServerVersion string           `json:"serverVersion,omitempty"`
	UpdatedAt     string           `json:"updatedAt,omitempty"`
	Tools         []ToolDefinition `json:"tools"`
}

type ToolDefinition struct {
	Name            string          `json:"name"`
	Description     string          `json:"description"`
	InputSchema     ToolInputSchema `json:"inputSchema"`
	ParameterSchema ToolInputSchema `json:"parameterSchema"`
}

type ToolInputSchema struct {
	Type       string                  `json:"type"`
	Properties map[string]ToolProperty `json:"properties"`
	Required   []string                `json:"required,omitempty"`
}

type ToolProperty struct {
	Type         string `json:"type"`
	Description  string `json:"description,omitempty"`
	Default      any    `json:"default,omitempty"`
	DefaultValue any    `json:"defaultValue,omitempty"`
	Hidden       bool   `json:"hidden,omitempty"`
	// SkillSourcedDescription marks a description that came from a skill parameter table, either read
	// live from the installed package or through the embedded catalog, which is generated from those
	// same tables. Help renders such text verbatim; a description with no skill behind it belongs to a
	// project-local custom command, whose author wrote it in the positive sense and which therefore
	// still needs a synthesized summary for a negated boolean flag. Never serialized: provenance is a
	// property of how this process loaded the catalog, not of the file.
	SkillSourcedDescription bool     `json:"-"`
	Enum                    []string `json:"enum,omitempty"`
	Items                   *struct {
		Type string `json:"type"`
	} `json:"items,omitempty"`
}

func (tool ToolDefinition) EffectiveInputSchema() ToolInputSchema {
	if tool.InputSchema.HasValues() {
		return tool.InputSchema
	}
	return tool.ParameterSchema
}

func (schema ToolInputSchema) HasValues() bool {
	return schema.Type != "" || len(schema.Properties) > 0 || len(schema.Required) > 0
}

func (property ToolProperty) EffectiveDefault() any {
	if property.Default != nil {
		return property.Default
	}
	return property.DefaultValue
}
