import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./app/**/*.{ts,tsx}",
    "./components/**/*.{ts,tsx}",
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ["Work Sans", "Inter", "ui-sans-serif", "system-ui", "sans-serif"],
      },
      colors: {
        // aa-theme surface (dark navy)
        surface: {
          50:  "rgb(217 222 226 / <alpha-value>)",
          100: "rgb(204 211 217 / <alpha-value>)",
          200: "rgb(191 200 207 / <alpha-value>)",
          300: "rgb(153 167 179 / <alpha-value>)",
          400: "rgb(77 100 121 / <alpha-value>)",
          500: "rgb(0 34 64 / <alpha-value>)",
          600: "rgb(0 31 58 / <alpha-value>)",
          700: "rgb(0 26 48 / <alpha-value>)",
          800: "rgb(0 20 38 / <alpha-value>)",
          900: "rgb(0 17 31 / <alpha-value>)",
        },
        // aa-theme primary (cyan)
        primary: {
          50:  "rgb(236 253 255 / <alpha-value>)",
          100: "rgb(230 253 255 / <alpha-value>)",
          200: "rgb(224 252 255 / <alpha-value>)",
          300: "rgb(205 250 254 / <alpha-value>)",
          400: "rgb(168 247 254 / <alpha-value>)",
          500: "rgb(131 243 253 / <alpha-value>)",
          600: "rgb(118 219 228 / <alpha-value>)",
          700: "rgb(98 182 190 / <alpha-value>)",
          800: "rgb(79 146 152 / <alpha-value>)",
          900: "rgb(64 119 124 / <alpha-value>)",
        },
        // aa-theme secondary (orange)
        secondary: {
          50:  "rgb(251 237 228 / <alpha-value>)",
          100: "rgb(250 231 219 / <alpha-value>)",
          200: "rgb(249 226 210 / <alpha-value>)",
          300: "rgb(245 208 183 / <alpha-value>)",
          400: "rgb(237 172 129 / <alpha-value>)",
          500: "rgb(229 137 75 / <alpha-value>)",
          600: "rgb(206 123 68 / <alpha-value>)",
          700: "rgb(172 103 56 / <alpha-value>)",
          800: "rgb(137 82 45 / <alpha-value>)",
          900: "rgb(112 67 37 / <alpha-value>)",
        },
        // error (red)
        error: {
          400: "rgb(223 97 100 / <alpha-value>)",
          500: "rgb(209 29 33 / <alpha-value>)",
          600: "rgb(188 26 30 / <alpha-value>)",
        },
        // success
        success: {
          400: "rgb(102 209 107 / <alpha-value>)",
          500: "rgb(36 189 44 / <alpha-value>)",
        },
        // warning
        warning: {
          400: "rgb(240 202 82 / <alpha-value>)",
          500: "rgb(234 179 8 / <alpha-value>)",
        },
      },
      borderRadius: {
        token: "4px",
      },
    },
  },
  plugins: [],
};

export default config;
