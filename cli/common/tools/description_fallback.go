package tools

// unityPlaceholderDescriptionPrefix is what Unity's schema generator emits for a property that
// carries no [Description] attribute (UnityCliLoopToolParameterSchemaGenerator.GetDescription
// returns "Parameter: <Name>"). The generated schema cache is therefore almost entirely
// placeholders, and the cache has no tool-level description field at all.
const unityPlaceholderDescriptionPrefix = "Parameter: "

// ApplyEmbeddedDescriptionFallback fills in the descriptions a synced project cache does not carry,
// using the catalog embedded in this binary. Without it, every option of every tool reads
// "Parameter: <Name>" inside a synced project while the same command outside a project shows real
// help — the cache was strictly worse than having no cache.
//
// Only description text is taken from the embedded catalog. Type, enum, default, required, and
// hidden always stay as the cache reported them, because Unity is the authority on what the running
// Editor actually accepts. The embedded text may come from an older or newer generation than the
// installed package, which is why it is a fallback and not a replacement.
func ApplyEmbeddedDescriptionFallback(catalog ToolCatalog) ToolCatalog {
	embedded := LoadDefault()

	for index, tool := range catalog.Tools {
		embeddedTool, ok := Find(embedded, tool.Name)
		if !ok {
			// A tool the embedded catalog does not know is a project-local custom command: its
			// author's own [Description] text is all there is, so it passes through untouched.
			continue
		}

		if tool.Description == "" {
			tool.Description = embeddedTool.Description
		}
		fillPlaceholderPropertyDescriptions(tool.EffectiveInputSchema(), embeddedTool.EffectiveInputSchema())
		catalog.Tools[index] = tool
	}

	return catalog
}

func fillPlaceholderPropertyDescriptions(schema ToolInputSchema, embeddedSchema ToolInputSchema) {
	for propertyName, property := range schema.Properties {
		if !isPlaceholderDescription(property.Description, propertyName) {
			// A real description means the schema author wrote one; overwriting it would discard
			// the more specific text in favor of this binary's generation.
			continue
		}

		embeddedProperty, ok := embeddedSchema.Properties[propertyName]
		if !ok || isPlaceholderDescription(embeddedProperty.Description, propertyName) {
			continue
		}

		property.Description = embeddedProperty.Description
		property.SkillSourcedDescription = embeddedProperty.SkillSourcedDescription
		schema.Properties[propertyName] = property
	}
}

func isPlaceholderDescription(description string, propertyName string) bool {
	return description == "" || description == unityPlaceholderDescriptionPrefix+propertyName
}
