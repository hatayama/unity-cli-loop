package domain

type ToolCatalog struct {
	Version       string           `json:"version"`
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
	Type         string   `json:"type"`
	Description  string   `json:"description,omitempty"`
	Default      any      `json:"default,omitempty"`
	DefaultValue any      `json:"DefaultValue,omitempty"`
	Enum         []string `json:"enum,omitempty"`
	Items        *struct {
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
