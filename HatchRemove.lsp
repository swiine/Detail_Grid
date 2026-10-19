;;; HatchRemove.lsp
;;; Command: HATCHREMOVE
;;; Selects every hatch in the drawing (model space and all layouts)
;;; and moves it onto the layer "_HATCH-REMOVE", creating the layer if needed.
;;; Hatches on locked layers are handled by temporarily unlocking those layers.

(vl-load-com)

(defun c:HATCHREMOVE (/ *error* doc layers lyrName ss i obj objLyr locked count)
  (setq doc     (vla-get-ActiveDocument (vlax-get-acad-object))
        layers  (vla-get-Layers doc)
        lyrName "_HATCH-REMOVE"
        locked  '()
        count   0)

  (defun *error* (msg)
    (foreach l locked (vla-put-Lock l :vlax-true))
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nError: " msg)))
    (princ))

  (vla-StartUndoMark doc)

  ;; Create the target layer if it doesn't exist, and make sure it is usable
  (if (not (tblsearch "LAYER" lyrName))
    (vla-Add layers lyrName))
  (setq obj (vla-Item layers lyrName))
  (vla-put-Lock obj :vlax-false)

  ;; Grab every hatch in the drawing (all spaces/layouts)
  (if (setq ss (ssget "_X" '((0 . "HATCH"))))
    (progn
      (setq i 0)
      (repeat (sslength ss)
        (setq obj    (vlax-ename->vla-object (ssname ss i))
              objLyr (vla-Item layers (vla-get-Layer obj)))
        ;; Temporarily unlock the hatch's current layer if needed
        (if (= (vla-get-Lock objLyr) :vlax-true)
          (progn
            (vla-put-Lock objLyr :vlax-false)
            (setq locked (cons objLyr locked))))
        (if (not (vl-catch-all-error-p
                   (vl-catch-all-apply 'vla-put-Layer (list obj lyrName))))
          (setq count (1+ count)))
        (setq i (1+ i)))

      ;; Re-lock any layers we unlocked
      (foreach l locked (vla-put-Lock l :vlax-true))
      (setq locked '())

      ;; Leave the hatches selected (current space only can be gripped)
      (sssetfirst nil (ssget "_X" (list '(0 . "HATCH") (cons 8 lyrName) (cons 410 (getvar "CTAB")))))

      (princ (strcat "\n" (itoa count) " hatch(es) moved to layer \"" lyrName "\".")))
    (princ "\nNo hatches found in the drawing."))

  (vla-EndUndoMark doc)
  (princ))

(princ "\nHatchRemove loaded. Type HATCHREMOVE to run.")
(princ)
