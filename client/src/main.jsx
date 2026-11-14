import React from "react";
import { createRoot } from "react-dom/client";

const root = createRoot(document.getElementById("root"));
if (window.location.hostname.startsWith("lms.")) {
  import("./legacy.jsx").then(({ LmsApp }) => root.render(<LmsApp />));
} else {
  import("./App.tsx").then(({ default: App }) => root.render(<App />));
}
