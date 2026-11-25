/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./Views/**/*.cshtml', './App_Code/**/*.cshtml', './Scripts/site.js', './Scripts/player.js', './Infrastructure/**/*.cs', './Services/**/*.cs', './Controllers/**/*.cs'],
  theme: {
    extend: {
      colors: {
        paper: '#F2EDE2',
        ink: '#0A0A0A',
        volt: '#FFE500',
        cobalt: '#2B3BFF',
        flame: '#FF4A1C',
        mint: '#8CF5C0',
      },
      fontFamily: {
        display: ['Anton', 'Impact', 'sans-serif'],
        sans: ['Archivo', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        mono: ['"Space Mono"', 'ui-monospace', 'monospace'],
      },
      boxShadow: {
        brut: '6px 6px 0 #0A0A0A',
        'brut-sm': '3px 3px 0 #0A0A0A',
        'brut-lg': '10px 10px 0 #0A0A0A',
      },
    },
  },
  plugins: [],
};
