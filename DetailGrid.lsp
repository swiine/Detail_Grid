;;; ---------------------------------------------------------------------
;;; DetailGrid.lsp
;;;
;;; Click a frame block (the block you use to line up viewports with a
;;; title block) and automatically insert a "grid" block on top of it,
;;; matched to the frame's insertion point, rotation, and scale - so the
;;; grid is always aligned no matter what sheet scale the frame is at.
;;;
;;; Commands:
;;;   DETAILGRID  (alias DG)  - click a frame block, insert/refresh its grid
;;;   DGRIDBLOCK              - click a block instance to use as the grid
;;;                             block from now on (instead of the default)
;;;
;;; Setup:
;;;   1. Draw your grid module as a block (default expected name:
;;;      detail_line) at true 1:1 size, with its insertion point at the
;;;      corner/center you want anchored to the frame's insertion point.
;;;   2. DetailGrid.cfg and the library .dwg holding that block live in
;;;      *dg:support-dir* below (a subfolder of where this .lsp lives).
;;;      Edit DetailGrid.cfg to point at the .dwg if it's not the
;;;      built-in default. If the block isn't already defined in the
;;;      current drawing, DETAILGRID imports it from that file
;;;      automatically.
;;;   3. If you named it something other than detail_line, either edit
;;;      DetailGrid.cfg, or run DGRIDBLOCK once and click an instance of
;;;      the block you want (session-only override).
;;;
;;; Usage:
;;;   Run DETAILGRID, click the frame block.
;;;     - If the frame is itself Annotative, DetailGrid leaves CANNOSCALE
;;;       (the document's current annotation scale) alone - the frame is
;;;       already showing at the right scale, or you couldn't have
;;;       clicked it, and an annotative grid block will match it too.
;;;     - If the frame is a plain scaled block, DetailGrid looks up its
;;;       X scale factor in the SCALE_MAP entries of DetailGrid.cfg and,
;;;       if one matches, switches CANNOSCALE to the mapped name - note
;;;       this is a document-wide setting change, not just local to the
;;;       new grid.
;;;   (AutoCAD's document-level scale list isn't reliably reachable via
;;;   classic ActiveX, hence SCALE_MAP instead of reading it directly.)
;;;   The grid is inserted on the DETAIL-GRID layer (created
;;;   automatically, non-plotting). Running DETAILGRID again on the same
;;;   frame replaces its grid instead of stacking up extra copies.
;;;
;;; Load: APPLOAD this file, or add (load "DetailGrid.lsp") to your
;;; acaddoc.lsp / Startup Suite to have it available in every drawing.
;;; ---------------------------------------------------------------------

(vl-load-com)

;; ---- configuration -----------------------------------------------------
;; Support folder holding DetailGrid.cfg and the grid's library .dwg
;; (DetailGrid.lsp itself lives one level up). Update this if that folder
;; moves.
(setq *dg:support-dir*
  "A:\\Civil\\AutoCAD\\Global\\Australia\\NSW\\_default\\Scripts\\_under_development\\_support\\Detail_Grid\\"
)

;; Built-in fallback defaults. DetailGrid.cfg, if found (see dg:load-config
;; below), overrides these - edit the .cfg file rather than this section
;; for day-to-day changes.
(if (not *dg:grid-block*) (setq *dg:grid-block* "detail_line"))
(if (not *dg:grid-source-dwg*)
  (setq *dg:grid-source-dwg* (strcat *dg:support-dir* "detail_grid.dwg"))
)
(if (not *dg:grid-layer*) (setq *dg:grid-layer* "DETAIL-GRID"))

;; (factor . CANNOSCALE-name) pairs, built from SCALE_MAP lines in
;; DetailGrid.cfg - used to translate a plain (non-annotative) frame's
;; scale factor into the matching annotation scale to switch to.
(setq *dg:scale-map* nil)

;; XDATA application name used to remember which grid belongs to which frame.
(setq *dg:xdata-app* "DETAILGRID")

;; ---- config file -----------------------------------------------------

(defun dg:trim (s) (vl-string-trim " \t" s))

;; Parse one "KEY=value" line from DetailGrid.cfg. Blank lines and lines
;; starting with ; or # are ignored. SCALE_MAP is special: its value is
;; itself "factor=CANNOSCALE-name", and repeated SCALE_MAP lines accumulate
;; into *dg:scale-map* rather than overwriting each other.
(defun dg:apply-config-line (line / eq-pos key val subEq)
  (setq line (dg:trim line))
  (if (and (> (strlen line) 0)
           (/= (substr line 1 1) ";")
           (/= (substr line 1 1) "#")
           (setq eq-pos (vl-string-search "=" line))
      )
    (progn
      (setq key (strcase (dg:trim (substr line 1 eq-pos))))
      (setq val (dg:trim (substr line (+ eq-pos 2))))
      (cond
        ((= key "GRID_BLOCK") (setq *dg:grid-block* val))
        ((= key "GRID_SOURCE_DWG") (setq *dg:grid-source-dwg* val))
        ((= key "GRID_LAYER") (setq *dg:grid-layer* val))
        ((= key "SCALE_MAP")
         (setq subEq (vl-string-search "=" val))
         (if subEq
           (setq *dg:scale-map*
             (cons
               (cons (atof (dg:trim (substr val 1 subEq))) (dg:trim (substr val (+ subEq 2))))
               *dg:scale-map*
             )
           )
         )
        )
      )
    )
  )
)

;; Look for DetailGrid.cfg in *dg:support-dir* first, then fall back to
;; AutoCAD's normal file search (current drawing's folder, Support File
;; Search Path, etc.), and apply any settings found.
(defun dg:load-config ( / path f line)
  (setq *dg:scale-map* nil)
  (setq path (findfile (strcat *dg:support-dir* "DetailGrid.cfg")))
  (if (not path) (setq path (findfile "DetailGrid.cfg")))
  (if path
    (progn
      (setq f (open path "r"))
      (if f
        (progn
          (while (setq line (read-line f))
            (dg:apply-config-line line)
          )
          (close f)
          (princ (strcat "\nDetailGrid: loaded settings from " path))
        )
      )
    )
  )
)

(dg:load-config)

;; ---- helpers -------------------------------------------------------------

(defun dg:ensure-app-registered ()
  (if (not (tblsearch "APPID" *dg:xdata-app*))
    (regapp *dg:xdata-app*)
  )
)

(defun dg:ensure-layer ( / doc lyr)
  (if (not (tblsearch "LAYER" *dg:grid-layer*))
    (progn
      (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
      (setq lyr (vla-Add (vla-get-Layers doc) *dg:grid-layer*))
      (vla-put-Color lyr 6)
      (vl-catch-all-apply 'vla-put-Plottable (list lyr :vlax-false))
    )
  )
)

;; Integer release number AutoCAD uses in its ObjectDBX ProgID, derived from
;; ACADVER (e.g. "24.3s (...)" -> 24).
(defun dg:acadver-major ( / ver i numstr)
  (setq ver (getvar "ACADVER"))
  (setq numstr "")
  (setq i 1)
  (while (and (<= i (strlen ver)) (wcmatch (substr ver i 1) "[0-9.]"))
    (setq numstr (strcat numstr (substr ver i 1)))
    (setq i (1+ i))
  )
  (atoi numstr)
)

;; Copy a single named block definition from an external .dwg into the
;; current drawing via an ObjectDBX side-database (does not insert an
;; instance - just makes the definition available). Returns T on success.
(defun dg:import-block-from-file (blkName srcPath / doc progid extDb opened srcBlocks srcBlk sa copyResult)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (setq progid (strcat "ObjectDBX.AxDbDocument." (itoa (dg:acadver-major))))
  (setq extDb (vl-catch-all-apply 'vla-GetInterfaceObject (list (vlax-get-acad-object) progid)))
  (cond
    ((or (null extDb) (vl-catch-all-error-p extDb))
     (princ (strcat "\nCouldn't create an ObjectDBX object (" progid ") to read " srcPath "."))
     nil
    )
    (t
     (setq opened (vl-catch-all-apply 'vla-Open (list extDb srcPath)))
     (cond
       ((vl-catch-all-error-p opened)
        (princ (strcat "\nCouldn't open " srcPath " - check the path in *dg:grid-source-dwg*."))
        nil
       )
       (t
        (setq srcBlocks (vla-get-Blocks extDb))
        (setq srcBlk (vl-catch-all-apply 'vla-Item (list srcBlocks blkName)))
        (cond
          ((or (null srcBlk) (vl-catch-all-error-p srcBlk))
           (princ (strcat "\nBlock \"" blkName "\" not found in " srcPath "."))
           nil
          )
          (t
           ;; vla-CopyObjects needs a real COM safearray of objects here -
           ;; a plain Lisp list won't coerce to a VARIANT array.
           (setq sa (vlax-make-safearray vlax-vbObject (cons 0 0)))
           (vlax-safearray-put-element sa 0 srcBlk)
           (setq copyResult (vl-catch-all-apply 'vla-CopyObjects (list extDb sa (vla-get-Blocks doc))))
           (if (vl-catch-all-error-p copyResult)
             (progn
               (princ (strcat "\nCopyObjects failed: " (vl-catch-all-error-message copyResult)))
               nil
             )
             T
           )
          )
        )
       )
     )
    )
  )
)

;; AcadDocument's "Scales" collection isn't available through classic
;; ActiveX Automation on every AutoCAD/vertical install (it errors with
;; "no function definition: VLA-GET-SCALES"), so factor<->CANNOSCALE-name
;; lookups go through *dg:scale-map* (built from DetailGrid.cfg SCALE_MAP
;; lines) instead of querying the drawing's scale list directly.

;; Find a CANNOSCALE name in *dg:scale-map* whose factor is close to the
;; given one. Returns nil if nothing in the map matches closely enough.
(defun dg:scale-name-for-factor (factor / pair best bestDiff diff)
  (setq best nil bestDiff nil)
  (foreach pair *dg:scale-map*
    (setq diff (abs (- (car pair) factor)))
    (if (or (null bestDiff) (< diff bestDiff))
      (progn (setq best (cdr pair)) (setq bestDiff diff))
    )
  )
  (if (and best (< bestDiff (max 0.0001 (* 0.0005 factor)))) best nil)
)

;; Reverse lookup: the factor mapped to a given CANNOSCALE name, or nil.
(defun dg:scale-factor-for-name (name / pair found)
  (setq found nil)
  (foreach pair *dg:scale-map*
    (if (and (not found) (= (cdr pair) name)) (setq found (car pair)))
  )
  found
)

;; Returns (xscale . yscale . scaleNameToSet) where scaleNameToSet is a
;; CANNOSCALE name to switch to (or nil to leave CANNOSCALE alone). If the
;; frame is annotative, it's already displaying at the current CANNOSCALE
;; (that's the only way you could see/click it), so nothing needs changing.
;; If it's a plain scaled block, its X scale factor is looked up in
;; *dg:scale-map* to find the matching CANNOSCALE name.
(defun dg:get-frame-scale (frameObj / sx sy scaleName)
  (if (and (vlax-property-available-p frameObj 'Annotative)
           (= (vla-get-Annotative frameObj) :vlax-true))
    (progn
      (setq scaleName nil)
      (setq sx (dg:scale-factor-for-name (getvar "CANNOSCALE")))
      (if (not sx) (setq sx 1.0))
      (setq sy sx)
    )
    (progn
      (setq sx (vla-get-XScaleFactor frameObj))
      (setq sy (vla-get-YScaleFactor frameObj))
      (setq scaleName (dg:scale-name-for-factor sx))
    )
  )
  (list sx sy scaleName)
)

;; Click-select a block reference. Returns its vla-object, or the symbol
;; 'cancel if the user backs out.
(defun dg:select-frame ( / ent obj etype result)
  (setq result nil)
  (while (not result)
    (setq ent (car (entsel "\nSelect (click) the frame block, or press Enter to cancel: ")))
    (cond
      ((null ent) (setq result 'cancel))
      (t
        (setq etype (cdr (assoc 0 (entget ent))))
        (if (= etype "INSERT")
          (setq result (vlax-ename->vla-object ent))
          (princ "\nThat's not a block reference - try again.")
        )
      )
    )
  )
  result
)

;; Read the handle of the grid block previously linked to this frame, if any.
(defun dg:get-linked-handle (ent / elist xdlist apppair h)
  (setq elist (entget ent (list *dg:xdata-app*)))
  (setq xdlist (cdr (assoc -3 elist)))
  (setq apppair (assoc *dg:xdata-app* xdlist))
  (setq h nil)
  (if apppair
    (foreach code (cdr apppair)
      (if (= (car code) 1000) (setq h (cdr code)))
    )
  )
  h
)

;; Store the grid block's handle on the frame entity as xdata.
(defun dg:set-xdata (ent handle)
  (entmod
    (append (entget ent) (list (list -3 (list *dg:xdata-app* (cons 1000 handle)))))
  )
)

;; Erase whatever grid block is currently linked to this frame, if it still exists.
(defun dg:remove-linked-grid (frameObj / ent h oldEnt)
  (setq ent (vlax-vla-object->ename frameObj))
  (setq h (dg:get-linked-handle ent))
  (if h
    (progn
      (setq oldEnt (handent h))
      (if (and oldEnt (entget oldEnt))
        (entdel oldEnt)
      )
    )
  )
)

;; ---- commands --------------------------------------------------------------

(defun c:DETAILGRID ( / *error* oldErr frameObj gridObj insPt rot scaleInfo sx sy scaleName doc ms)
  (setq oldErr *error*)
  (defun *error* (msg)
    (if (and msg (not (member msg '("Function cancelled" "quit / exit abort"))))
      (princ (strcat "\nDetailGrid error: " msg))
    )
    (setvar "CMDECHO" 1)
    (setq *error* oldErr)
    (princ)
  )

  (setvar "CMDECHO" 0)
  (dg:ensure-app-registered)
  (dg:ensure-layer)

  (if (and (not (tblsearch "BLOCK" *dg:grid-block*))
           (dg:import-block-from-file *dg:grid-block* *dg:grid-source-dwg*))
    (princ (strcat "\nImported \"" *dg:grid-block* "\" from " *dg:grid-source-dwg* "."))
  )

  (if (not (tblsearch "BLOCK" *dg:grid-block*))
    (progn
      (princ (strcat "\nGrid block \"" *dg:grid-block* "\" isn't defined in this drawing"))
      (princ (strcat " and couldn't be imported from " *dg:grid-source-dwg* "."))
      (princ "\nCheck *dg:grid-source-dwg*, or run DGRIDBLOCK to point DetailGrid at a different block.")
    )
    (progn
      (setq frameObj (dg:select-frame))
      (if (eq frameObj 'cancel)
        (princ "\nCancelled.")
        (progn
          (setq insPt (vla-get-InsertionPoint frameObj))
          (setq rot (vla-get-Rotation frameObj))
          (setq scaleInfo (dg:get-frame-scale frameObj))
          (setq sx (nth 0 scaleInfo) sy (nth 1 scaleInfo) scaleName (nth 2 scaleInfo))

          ;; Match the document's annotation scale to the frame's scale, so
          ;; an annotative grid block displays at the right size on its own.
          ;; (scaleName is nil when the frame is itself annotative - it's
          ;; already showing at the correct CANNOSCALE, or there's no
          ;; SCALE_MAP entry for a plain frame's scale factor.)
          (cond
            ((and scaleName (/= scaleName (getvar "CANNOSCALE")))
             (setvar "CANNOSCALE" scaleName)
             (princ (strcat "\nAnnotation scale set to " scaleName " to match frame."))
            )
            ((and (not scaleName)
                  (not (and (vlax-property-available-p frameObj 'Annotative)
                            (= (vla-get-Annotative frameObj) :vlax-true))))
             (princ
               (strcat "\nNo SCALE_MAP entry in DetailGrid.cfg matches the frame's "
                       (rtos sx 2 4) ":1 factor - add one if the grid block is annotative.")
             )
            )
          )

          (dg:remove-linked-grid frameObj)

          (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
          (setq ms (vla-get-ModelSpace doc))
          (setq gridObj (vla-InsertBlock ms insPt *dg:grid-block* 1.0 1.0 1.0 rot))
          (vla-put-Layer gridObj *dg:grid-layer*)

          ;; Only non-annotative grid blocks need their scale set directly -
          ;; an annotative one already sizes itself from CANNOSCALE above.
          (if (not (and (vlax-property-available-p gridObj 'Annotative)
                        (= (vla-get-Annotative gridObj) :vlax-true)))
            (progn
              (vla-put-XScaleFactor gridObj sx)
              (vla-put-YScaleFactor gridObj sy)
            )
          )

          (dg:set-xdata (vlax-vla-object->ename frameObj) (vla-get-Handle gridObj))
          (vla-Update gridObj)

          (princ
            (strcat "\nGrid \"" *dg:grid-block* "\" aligned to frame - scale "
                    (rtos sx 2 4) ", rotation " (angtos rot) ".")
          )
        )
      )
    )
  )

  (setvar "CMDECHO" 1)
  (setq *error* oldErr)
  (princ)
)

(defun c:DG () (c:DETAILGRID))

(defun c:DGRIDBLOCK ( / ent obj name)
  (setq ent (car (entsel "\nClick an instance of the block to use as the grid block: ")))
  (if (and ent (= (cdr (assoc 0 (entget ent))) "INSERT"))
    (progn
      (setq obj (vlax-ename->vla-object ent))
      (setq name (vla-get-EffectiveName obj))
      (setq *dg:grid-block* name)
      (princ (strcat "\nDetailGrid will now use block \"" name "\" as the grid block."))
    )
    (princ "\nNo block selected - grid block unchanged.")
  )
  (princ)
)

(princ "\nDetailGrid loaded - type DETAILGRID (or DG) to align a grid to a frame block, DGRIDBLOCK to choose the grid block.")
(princ)
